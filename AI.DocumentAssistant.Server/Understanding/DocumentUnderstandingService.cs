using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Rag;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Understanding;

public sealed class DocumentUnderstandingService : IDocumentUnderstandingService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IDocumentUnderstandingInputBuilder _inputBuilder;
    private readonly IDocumentUnderstandingClient _client;
    private readonly DocumentUnderstandingValidator _validator;
    private readonly OpenAIDocumentUnderstandingOptions _options;
    private readonly OpenAIAnswerOptions _answerOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DocumentUnderstandingService> _logger;

    public DocumentUnderstandingService(
        ApplicationDbContext dbContext,
        IDocumentUnderstandingInputBuilder inputBuilder,
        IDocumentUnderstandingClient client,
        DocumentUnderstandingValidator validator,
        IOptions<OpenAIDocumentUnderstandingOptions> options,
        IOptions<OpenAIAnswerOptions> answerOptions,
        TimeProvider timeProvider,
        ILogger<DocumentUnderstandingService> logger)
    {
        _dbContext = dbContext;
        _inputBuilder = inputBuilder;
        _client = client;
        _validator = validator;
        _options = options.Value;
        _answerOptions = answerOptions.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<DocumentUnderstandingRunResult> AnalyzePersistedAsync(
        Guid documentId,
        bool force,
        CancellationToken cancellationToken)
    {
        var sourceSections = await _dbContext.DocumentTextSections
            .AsNoTracking()
            .Where(section => section.DocumentId == documentId)
            .OrderBy(section => section.SectionIndex)
            .Select(section => new DocumentUnderstandingSourceSection(
                section.SectionIndex,
                section.NormalizedContent ?? string.Empty,
                section.PageNumber,
                section.SectionTitle))
            .ToListAsync(cancellationToken);

        return await AnalyzeAsync(
            documentId,
            sourceSections,
            force,
            cancellationToken);
    }

    public async Task StagePendingIfStaleAsync(
        Guid documentId,
        IReadOnlyList<DocumentUnderstandingSourceSection> sourceSections,
        CancellationToken cancellationToken)
    {
        var input = _inputBuilder.Build(sourceSections, cancellationToken);
        var model = ResolveModel();
        var promptVersion = DocumentUnderstandingArchitecture.PromptVersion;
        var understanding = await _dbContext.DocumentUnderstandings
            .SingleOrDefaultAsync(
                item => item.DocumentId == documentId,
                cancellationToken);

        if (understanding is null)
        {
            _dbContext.DocumentUnderstandings.Add(new DocumentUnderstanding
            {
                DocumentId = documentId,
                Status = DocumentUnderstandingStatus.Pending,
                Model = model,
                PromptVersion = promptVersion,
                SourceContentHash = input.SourceContentHash
            });
            return;
        }

        if (understanding.Status == DocumentUnderstandingStatus.Processing)
        {
            throw new DocumentUnderstandingException(
                "Document understanding is already processing.");
        }

        if (MatchesVersion(
                understanding,
                input.SourceContentHash,
                model,
                promptVersion))
        {
            return;
        }

        understanding.Status = DocumentUnderstandingStatus.Pending;
        understanding.Model = model;
        understanding.PromptVersion = promptVersion;
        understanding.SourceContentHash = input.SourceContentHash;
        understanding.LastError = null;
    }

    public async Task<DocumentUnderstandingRunResult> AnalyzeAsync(
        Guid documentId,
        IReadOnlyList<DocumentUnderstandingSourceSection> sourceSections,
        bool force,
        CancellationToken cancellationToken)
    {
        var input = _inputBuilder.Build(sourceSections, cancellationToken);
        var model = ResolveModel();
        var promptVersion = DocumentUnderstandingArchitecture.PromptVersion;

        var existing = await _dbContext.DocumentUnderstandings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                understanding => understanding.DocumentId == documentId,
                cancellationToken);

        if (!force &&
            existing is not null &&
            existing.Status is DocumentUnderstandingStatus.Ready or
                DocumentUnderstandingStatus.Skipped &&
            MatchesVersion(existing, input.SourceContentHash, model, promptVersion))
        {
            return new DocumentUnderstandingRunResult(
                existing.Status,
                input.SourceContentHash,
                model,
                promptVersion,
                true);
        }

        await ClaimProcessingAsync(
            documentId,
            input.SourceContentHash,
            model,
            promptVersion,
            existing,
            cancellationToken);

        if (!input.HasSufficientText)
        {
            await PersistSkippedAsync(
                documentId,
                input.SourceContentHash,
                model,
                promptVersion,
                input.SkipReason ?? DocumentUnderstandingArchitecture.InsufficientTextReason,
                cancellationToken);
            return new DocumentUnderstandingRunResult(
                DocumentUnderstandingStatus.Skipped,
                input.SourceContentHash,
                model,
                promptVersion,
                false);
        }

        try
        {
            _logger.LogInformation(
                "Analyzing document {DocumentId} using model {UnderstandingModel}, prompt {PromptVersion}, and a bounded {InputTokenCount}-token {InputKind} input derived from {FullTokenCount} normalized tokens.",
                documentId,
                model,
                promptVersion,
                input.InputTokenCount,
                input.IsSampled ? "representative sample" : "full-text",
                input.FullTokenCount);

            var providerResult = await _client.AnalyzeAsync(
                model,
                input.Content,
                cancellationToken);
            var validated = _validator.Validate(providerResult);
            await PersistSuccessAsync(
                documentId,
                input.SourceContentHash,
                model,
                promptVersion,
                validated,
                cancellationToken);

            return new DocumentUnderstandingRunResult(
                DocumentUnderstandingStatus.Ready,
                input.SourceContentHash,
                model,
                promptVersion,
                false);
        }
        catch (OperationCanceledException)
        {
            await TryMarkFailedAsync(
                documentId,
                input.SourceContentHash,
                model,
                promptVersion,
                "Document understanding was interrupted. Please retry.");
            throw;
        }
        catch (DocumentUnderstandingException exception)
        {
            await TryMarkFailedAsync(
                documentId,
                input.SourceContentHash,
                model,
                promptVersion,
                exception.SafeMessage);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Document understanding failed for document {DocumentId} with exception type {ExceptionType}. Document content and provider payloads were omitted.",
                documentId,
                exception.GetType().FullName);
            await TryMarkFailedAsync(
                documentId,
                input.SourceContentHash,
                model,
                promptVersion,
                DocumentUnderstandingArchitecture.SafeFailureMessage);
            throw new DocumentUnderstandingException(
                DocumentUnderstandingArchitecture.SafeFailureMessage,
                exception);
        }
    }

    private async Task ClaimProcessingAsync(
        Guid documentId,
        string contentHash,
        string model,
        string promptVersion,
        DocumentUnderstanding? existing,
        CancellationToken cancellationToken)
    {
        if (existing is null)
        {
            _dbContext.DocumentUnderstandings.Add(new DocumentUnderstanding
            {
                DocumentId = documentId,
                Status = DocumentUnderstandingStatus.Processing,
                Model = model,
                PromptVersion = promptVersion,
                SourceContentHash = contentHash
            });

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException exception)
            {
                _logger.LogWarning(
                    "Document understanding claim collided for document {DocumentId}; provider details were omitted.",
                    documentId);
                throw new DocumentUnderstandingException(
                    "Document understanding is already processing.",
                    exception);
            }
        }

        if (_dbContext.Database.IsRelational())
        {
            var updated = await _dbContext.DocumentUnderstandings
                .Where(understanding =>
                    understanding.DocumentId == documentId &&
                    understanding.Status != DocumentUnderstandingStatus.Processing)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        understanding => understanding.Status,
                        DocumentUnderstandingStatus.Processing)
                    .SetProperty(understanding => understanding.Model, model)
                    .SetProperty(understanding => understanding.PromptVersion, promptVersion)
                    .SetProperty(understanding => understanding.SourceContentHash, contentHash)
                    .SetProperty(understanding => understanding.LastError, (string?)null),
                    cancellationToken);

            if (updated != 1)
            {
                throw new DocumentUnderstandingException(
                    "Document understanding is already processing.");
            }

            var locallyTracked = _dbContext.DocumentUnderstandings.Local
                .SingleOrDefault(understanding =>
                    understanding.DocumentId == documentId);
            if (locallyTracked is not null)
            {
                locallyTracked.Status = DocumentUnderstandingStatus.Processing;
                locallyTracked.Model = model;
                locallyTracked.PromptVersion = promptVersion;
                locallyTracked.SourceContentHash = contentHash;
                locallyTracked.LastError = null;
                _dbContext.Entry(locallyTracked).State = EntityState.Unchanged;
            }

            return;
        }

        var tracked = await _dbContext.DocumentUnderstandings
            .SingleAsync(
                understanding => understanding.DocumentId == documentId,
                cancellationToken);
        if (tracked.Status == DocumentUnderstandingStatus.Processing)
        {
            throw new DocumentUnderstandingException(
                "Document understanding is already processing.");
        }

        tracked.Status = DocumentUnderstandingStatus.Processing;
        tracked.Model = model;
        tracked.PromptVersion = promptVersion;
        tracked.SourceContentHash = contentHash;
        tracked.LastError = null;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistSuccessAsync(
        Guid documentId,
        string contentHash,
        string model,
        string promptVersion,
        ValidatedDocumentUnderstanding validated,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionIfSupportedAsync(cancellationToken);
        var understanding = await LoadCurrentAttemptAsync(
            documentId,
            contentHash,
            model,
            promptVersion,
            cancellationToken);
        await DeleteMetadataAsync(documentId, cancellationToken);

        understanding.Status = DocumentUnderstandingStatus.Ready;
        understanding.DocumentType = validated.DocumentType;
        understanding.DocumentSubtype = validated.DocumentSubtype;
        understanding.DocumentTypeConfidence = validated.DocumentTypeConfidence;
        understanding.PrimaryLanguageCode = validated.PrimaryLanguageCode;
        understanding.LanguageConfidence = validated.LanguageConfidence;
        understanding.DetectedTitle = validated.DetectedTitle;
        understanding.Subject = validated.Subject;
        understanding.Model = model;
        understanding.PromptVersion = promptVersion;
        understanding.SourceContentHash = contentHash;
        understanding.AnalyzedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        understanding.LastError = null;

        _dbContext.DocumentMetadataEntries.AddRange(validated.Metadata.Select(entry =>
            new DocumentMetadataEntry
            {
                Id = Guid.NewGuid(),
                DocumentUnderstandingId = documentId,
                Kind = entry.Kind,
                Label = entry.Label,
                Value = entry.Value,
                NormalizedValue = entry.NormalizedValue,
                Confidence = entry.Confidence,
                Sequence = entry.Sequence
            }));

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private async Task PersistSkippedAsync(
        Guid documentId,
        string contentHash,
        string model,
        string promptVersion,
        string reason,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionIfSupportedAsync(cancellationToken);
        var understanding = await LoadCurrentAttemptAsync(
            documentId,
            contentHash,
            model,
            promptVersion,
            cancellationToken);
        await DeleteMetadataAsync(documentId, cancellationToken);

        understanding.Status = DocumentUnderstandingStatus.Skipped;
        understanding.DocumentType = null;
        understanding.DocumentSubtype = null;
        understanding.DocumentTypeConfidence = null;
        understanding.PrimaryLanguageCode = null;
        understanding.LanguageConfidence = null;
        understanding.DetectedTitle = null;
        understanding.Subject = null;
        understanding.AnalyzedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        understanding.LastError = Truncate(reason);

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private async Task<DocumentUnderstanding> LoadCurrentAttemptAsync(
        Guid documentId,
        string contentHash,
        string model,
        string promptVersion,
        CancellationToken cancellationToken)
    {
        var understanding = await _dbContext.DocumentUnderstandings
            .SingleOrDefaultAsync(
                item => item.DocumentId == documentId,
                cancellationToken);
        if (understanding is null ||
            understanding.Status != DocumentUnderstandingStatus.Processing ||
            !MatchesVersion(understanding, contentHash, model, promptVersion))
        {
            throw new DocumentUnderstandingException(
                "Document understanding changed while it was being analyzed.");
        }

        return understanding;
    }

    private async Task TryMarkFailedAsync(
        Guid documentId,
        string contentHash,
        string model,
        string promptVersion,
        string safeMessage)
    {
        try
        {
            var understanding = await _dbContext.DocumentUnderstandings
                .SingleOrDefaultAsync(item => item.DocumentId == documentId);
            if (understanding is null ||
                understanding.Status != DocumentUnderstandingStatus.Processing ||
                !MatchesVersion(understanding, contentHash, model, promptVersion))
            {
                return;
            }

            understanding.Status = DocumentUnderstandingStatus.Failed;
            understanding.AnalyzedAtUtc = null;
            understanding.LastError = Truncate(safeMessage);
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Could not persist Failed document-understanding state for document {DocumentId}.",
                documentId);
        }
    }

    private async Task DeleteMetadataAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.DocumentMetadataEntries
            .Where(entry => entry.DocumentUnderstandingId == documentId);
        if (_dbContext.Database.IsRelational())
        {
            await query.ExecuteDeleteAsync(cancellationToken);
            return;
        }

        _dbContext.DocumentMetadataEntries.RemoveRange(
            await query.ToListAsync(cancellationToken));
    }

    private string ResolveModel()
    {
        var model = string.IsNullOrWhiteSpace(_options.DocumentUnderstandingModel)
            ? _answerOptions.AnswerModel
            : _options.DocumentUnderstandingModel;
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new DocumentUnderstandingException(
                "Document understanding service configuration is unavailable.");
        }

        return model.Trim();
    }

    private static bool MatchesVersion(
        DocumentUnderstanding understanding,
        string contentHash,
        string model,
        string promptVersion) =>
        string.Equals(understanding.SourceContentHash, contentHash, StringComparison.Ordinal) &&
        string.Equals(understanding.Model, model, StringComparison.Ordinal) &&
        string.Equals(understanding.PromptVersion, promptVersion, StringComparison.Ordinal);

    private Task<IDbContextTransaction?> BeginTransactionIfSupportedAsync(
        CancellationToken cancellationToken) =>
        _dbContext.Database.IsRelational()
            ? BeginRelationalTransactionAsync(cancellationToken)
            : Task.FromResult<IDbContextTransaction?>(null);

    private async Task<IDbContextTransaction?> BeginRelationalTransactionAsync(
        CancellationToken cancellationToken) =>
        await _dbContext.Database.BeginTransactionAsync(cancellationToken);

    private static string Truncate(string value) =>
        value.Length <= DocumentUnderstandingLimits.MaximumErrorLength
            ? value
            : value[..DocumentUnderstandingLimits.MaximumErrorLength];
}
