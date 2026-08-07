using System.Diagnostics;
using AI.DocumentAssistant.Server.Chunking;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Embeddings;
using AI.DocumentAssistant.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Normalization;

public sealed class DocumentNormalizationService : IDocumentNormalizationService
{
    private const int MaximumErrorLength = 500;
    private const string PdfContentType = "application/pdf";

    private readonly ApplicationDbContext _dbContext;
    private readonly IDocumentTextNormalizer _normalizer;
    private readonly IDocumentChunkGenerator _chunkGenerator;
    private readonly ITextEmbeddingService _embeddingService;
    private readonly OpenAIEmbeddingOptions _embeddingOptions;
    private readonly ILogger<DocumentNormalizationService> _logger;

    public DocumentNormalizationService(
        ApplicationDbContext dbContext,
        IDocumentTextNormalizer normalizer,
        IDocumentChunkGenerator chunkGenerator,
        ITextEmbeddingService embeddingService,
        IOptions<OpenAIEmbeddingOptions> embeddingOptions,
        ILogger<DocumentNormalizationService> logger)
    {
        _dbContext = dbContext;
        _normalizer = normalizer;
        _chunkGenerator = chunkGenerator;
        _embeddingService = embeddingService;
        _embeddingOptions = embeddingOptions.Value;
        _logger = logger;
    }

    public async Task<DocumentNormalizationRebuildResult> RebuildAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        Document? document = null;

        try
        {
            document = await _dbContext.Documents
                .SingleOrDefaultAsync(item => item.Id == documentId, cancellationToken)
                ?? throw new DocumentNormalizationException(
                    "The document is not available for normalization.");

            var sections = await _dbContext.DocumentTextSections
                .Where(section => section.DocumentId == documentId)
                .OrderBy(section => section.SectionIndex)
                .ToListAsync(cancellationToken);

            if (sections.Count == 0)
            {
                throw new DocumentNormalizationException(
                    "Extracted text is required before normalization can be rebuilt.");
            }

            _logger.LogInformation(
                "Rebuilding normalization for document {DocumentId} in project {ProjectId} from {SourceSectionCount} stored raw sections.",
                document.Id,
                document.ProjectId,
                sections.Count);

            var result = _normalizer.Normalize(
                sections.Select(section => new NormalizationSourceSection(
                    section.SectionIndex,
                    section.Content,
                    section.PageNumber,
                    section.SectionTitle)).ToArray(),
                string.Equals(document.ContentType, PdfContentType, StringComparison.OrdinalIgnoreCase),
                cancellationToken);

            var normalizedByIndex = result.Sections.ToDictionary(
                section => section.SectionIndex);
            var generatedChunks = _chunkGenerator.Generate(
                result.Sections.Select(section => new ChunkSourceSection(
                    section.SectionIndex,
                    section.Content,
                    section.PageNumber,
                    section.SectionTitle)).ToArray(),
                cancellationToken);

            var embeddingResult = await _embeddingService.GenerateEmbeddingsAsync(
                generatedChunks.Select(chunk => chunk.Content).ToArray(),
                cancellationToken);
            EmbeddingResultValidator.Validate(
                embeddingResult,
                generatedChunks.Count,
                _embeddingOptions.EmbeddingModel,
                EmbeddingArchitecture.Dimensions);

            await using var transaction = await BeginTransactionIfSupportedAsync(cancellationToken);
            await DeleteExistingChunksAsync(documentId, cancellationToken);

            var completedAtUtc = DateTime.UtcNow;
            foreach (var section in sections)
            {
                var normalized = normalizedByIndex[section.SectionIndex];
                section.NormalizedContent = normalized.Content;
                section.NormalizationChanged = normalized.Changed;
                section.RemovedCharacterCount = normalized.RemovedCharacterCount;
                section.NormalizedAtUtc = completedAtUtc;
            }

            var chunks = generatedChunks.Select(chunk => new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                ChunkIndex = chunk.ChunkIndex,
                Content = chunk.Content,
                CharacterCount = chunk.CharacterCount,
                TokenCount = chunk.TokenCount,
                PageStart = chunk.PageStart,
                PageEnd = chunk.PageEnd,
                SectionTitle = Truncate(chunk.SectionTitle, 500),
                SourceSectionStartIndex = chunk.SourceSectionStartIndex,
                SourceSectionEndIndex = chunk.SourceSectionEndIndex,
                CreatedAtUtc = completedAtUtc
            }).ToArray();

            for (var index = 0; index < chunks.Length; index++)
            {
                EmbeddingPersistence.ApplyToChunk(
                    chunks[index],
                    embeddingResult.Embeddings[index],
                    embeddingResult,
                    completedAtUtc);
            }

            _dbContext.DocumentChunks.AddRange(chunks);

            document.Status = DocumentStatus.Ready;
            document.ProcessedAtUtc ??= completedAtUtc;
            document.ProcessingError = null;
            document.NormalizedCharacterCount = result.NormalizedCharacterCount;
            document.NormalizationRemovedCharacterCount = result.RemovedCharacterCount;
            document.NormalizationChangedSectionCount = result.ChangedSectionCount;
            document.NormalizedAtUtc = completedAtUtc;
            document.NormalizationError = null;
            document.ChunkCount = chunks.Length;
            document.ChunkedAtUtc = completedAtUtc;
            document.ChunkingError = null;
            EmbeddingPersistence.ApplyToDocument(
                document,
                chunks.Length,
                embeddingResult,
                completedAtUtc);
            document.UpdatedAtUtc = completedAtUtc;

            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            stopwatch.Stop();
            _logger.LogInformation(
                "Normalization rebuild completed for document {DocumentId} with status Ready in {ElapsedMilliseconds} ms. PDF pages: {PdfPageCount}; source sections: {SourceSectionCount}; candidate blocks: {CandidateBlockCount}; confirmed repeated blocks: {ConfirmedRepeatedBlockCount}; changed sections: {ChangedSectionCount}; removed characters: {RemovedCharacterCount}; original characters: {OriginalCharacterCount}; normalized characters: {NormalizedCharacterCount}; embedded chunks: {ChunkCount}; embedding model: {EmbeddingModel}; embedding dimensions: {EmbeddingDimensions}.",
                document.Id,
                stopwatch.ElapsedMilliseconds,
                result.PdfPageCount,
                sections.Count,
                result.CandidateBlockCount,
                result.ConfirmedRepeatedBlockCount,
                result.ChangedSectionCount,
                result.RemovedCharacterCount,
                result.OriginalCharacterCount,
                result.NormalizedCharacterCount,
                chunks.Length,
                embeddingResult.Model,
                embeddingResult.Dimensions);

            return new DocumentNormalizationRebuildResult(
                result.ChangedSectionCount,
                result.RemovedCharacterCount,
                result.NormalizedCharacterCount,
                chunks.Length,
                completedAtUtc);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            var safeMessage = exception switch
            {
                DocumentNormalizationException normalizationException => normalizationException.SafeMessage,
                DocumentChunkingException chunkingException => chunkingException.SafeMessage,
                DocumentEmbeddingException embeddingException => embeddingException.SafeMessage,
                OperationCanceledException => "Normalization was interrupted. Please retry.",
                _ => "Document normalization failed. Please retry."
            };

            try
            {
                await RestoreReadyAfterFailureAsync(
                    documentId,
                    safeMessage,
                    exception is DocumentChunkingException,
                    exception is DocumentEmbeddingException);
            }
            catch (Exception failureUpdateException)
            {
                _logger.LogError(
                    failureUpdateException,
                    "Could not persist normalization failure for document {DocumentId}.",
                    documentId);
            }

            _logger.LogError(
                exception,
                "Normalization rebuild failed for document {DocumentId} in project {ProjectId} after {ElapsedMilliseconds} ms; the previous authoritative normalization, chunks, and embeddings were preserved.",
                documentId,
                document?.ProjectId,
                stopwatch.ElapsedMilliseconds);

            if (exception is OperationCanceledException)
            {
                throw;
            }

            throw exception is DocumentEmbeddingException preservedEmbeddingException
                ? preservedEmbeddingException
                : new DocumentNormalizationException(safeMessage, exception);
        }
    }

    private async Task RestoreReadyAfterFailureAsync(
        Guid documentId,
        string safeMessage,
        bool chunkingFailed,
        bool embeddingFailed)
    {
        _dbContext.ChangeTracker.Clear();
        await using var transaction = await BeginTransactionIfSupportedAsync(CancellationToken.None);
        var document = await _dbContext.Documents.SingleOrDefaultAsync(item => item.Id == documentId);
        if (document is null)
        {
            return;
        }

        document.Status = DocumentStatus.Ready;
        document.NormalizationError = !chunkingFailed && !embeddingFailed
            ? Truncate(safeMessage, MaximumErrorLength)
            : null;
        document.ChunkingError = chunkingFailed
            ? Truncate(safeMessage, MaximumErrorLength)
            : null;
        document.EmbeddingError = embeddingFailed
            ? Truncate(safeMessage, MaximumErrorLength)
            : null;
        document.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        if (transaction is not null)
        {
            await transaction.CommitAsync();
        }
    }

    private async Task DeleteExistingChunksAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var query = _dbContext.DocumentChunks.Where(chunk => chunk.DocumentId == documentId);
        if (_dbContext.Database.IsRelational())
        {
            await query.ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            _dbContext.DocumentChunks.RemoveRange(await query.ToListAsync(cancellationToken));
        }
    }

    private Task<IDbContextTransaction?> BeginTransactionIfSupportedAsync(CancellationToken cancellationToken) =>
        _dbContext.Database.IsRelational()
            ? BeginRelationalTransactionAsync(cancellationToken)
            : Task.FromResult<IDbContextTransaction?>(null);

    private async Task<IDbContextTransaction?> BeginRelationalTransactionAsync(CancellationToken cancellationToken) =>
        await _dbContext.Database.BeginTransactionAsync(cancellationToken);

    private static string? Truncate(string? value, int maximumLength) =>
        value is null || value.Length <= maximumLength ? value : value[..maximumLength];
}
