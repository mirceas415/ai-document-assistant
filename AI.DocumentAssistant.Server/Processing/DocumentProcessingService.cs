using System.Diagnostics;
using AI.DocumentAssistant.Server.Chunking;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Embeddings;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Normalization;
using AI.DocumentAssistant.Server.Storage;
using AI.DocumentAssistant.Server.Understanding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Processing;

public sealed class DocumentProcessingService : IDocumentProcessingService
{
    private const int MaximumErrorLength = 500;

    private readonly ApplicationDbContext _dbContext;
    private readonly IFileStorageService _fileStorage;
    private readonly IReadOnlyList<IDocumentTextExtractor> _extractors;
    private readonly IDocumentTextNormalizer _normalizer;
    private readonly IDocumentChunkGenerator _chunkGenerator;
    private readonly ITextEmbeddingService _embeddingService;
    private readonly IDocumentUnderstandingService _understandingService;
    private readonly OpenAIEmbeddingOptions _embeddingOptions;
    private readonly ILogger<DocumentProcessingService> _logger;

    public DocumentProcessingService(
        ApplicationDbContext dbContext,
        IFileStorageService fileStorage,
        IEnumerable<IDocumentTextExtractor> extractors,
        IDocumentTextNormalizer normalizer,
        IDocumentChunkGenerator chunkGenerator,
        ITextEmbeddingService embeddingService,
        IDocumentUnderstandingService understandingService,
        IOptions<OpenAIEmbeddingOptions> embeddingOptions,
        ILogger<DocumentProcessingService> logger)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _extractors = extractors.ToArray();
        _normalizer = normalizer;
        _chunkGenerator = chunkGenerator;
        _embeddingService = embeddingService;
        _understandingService = understandingService;
        _embeddingOptions = embeddingOptions.Value;
        _logger = logger;
    }

    public async Task ProcessAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await _dbContext.Documents
            .SingleOrDefaultAsync(item => item.Id == documentId, cancellationToken);

        if (document is null)
        {
            _logger.LogWarning(
                "Document {DocumentId} was not found when background processing began.",
                documentId);
            return;
        }

        if (document.Status is not (DocumentStatus.Uploaded or DocumentStatus.Failed))
        {
            _logger.LogInformation(
                "Skipping document {DocumentId} in status {DocumentStatus}.",
                document.Id,
                document.Status);
            return;
        }

        document.Status = DocumentStatus.Processing;
        document.ProcessingStartedAtUtc = DateTime.UtcNow;
        document.ProcessedAtUtc = null;
        document.ProcessingError = null;
        document.ExtractedCharacterCount = 0;
        document.ExtractedSectionCount = 0;
        document.NormalizedCharacterCount = 0;
        document.NormalizationRemovedCharacterCount = 0;
        document.NormalizationChangedSectionCount = 0;
        document.NormalizedAtUtc = null;
        document.NormalizationError = null;
        document.ChunkCount = 0;
        document.ChunkedAtUtc = null;
        document.ChunkingError = null;
        EmbeddingPersistence.ClearDocumentMetadata(document);
        document.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var extension = Path.GetExtension(document.StoredFileName);
        var extractor = _extractors.SingleOrDefault(candidate =>
            candidate.CanProcess(document.ContentType, extension));

        try
        {
            if (extractor is null)
            {
                throw new DocumentExtractionException(
                    "No text extractor is available for this document type.");
            }

            _logger.LogInformation(
                "Extracting document {DocumentId} in project {ProjectId} with {ExtractorType}.",
                document.Id,
                document.ProjectId,
                extractor.GetType().Name);

            await using var documentStream = await _fileStorage.OpenReadAsync(
                document.StoredFileName,
                cancellationToken);

            var extractedSections = await extractor.ExtractAsync(
                documentStream,
                cancellationToken);

            if (extractedSections.Count == 0)
            {
                throw new DocumentExtractionException(
                    "No extractable text was found. OCR is not supported yet.");
            }

            var normalizationStopwatch = Stopwatch.StartNew();
            DocumentNormalizationResult normalizationResult;
            try
            {
                normalizationResult = _normalizer.Normalize(
                    extractedSections.Select(section => new NormalizationSourceSection(
                        section.SectionIndex,
                        section.Content,
                        section.PageNumber,
                        section.SectionTitle)).ToArray(),
                    string.Equals(document.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new DocumentNormalizationException(
                    "Document normalization failed. Please retry.",
                    exception);
            }
            normalizationStopwatch.Stop();

            _logger.LogInformation(
                "Normalized document {DocumentId} in {ElapsedMilliseconds} ms. PDF pages: {PdfPageCount}; source sections: {SourceSectionCount}; candidate blocks: {CandidateBlockCount}; confirmed repeated blocks: {ConfirmedRepeatedBlockCount}; changed sections: {ChangedSectionCount}; removed characters: {RemovedCharacterCount}; original characters: {OriginalCharacterCount}; normalized characters: {NormalizedCharacterCount}.",
                document.Id,
                normalizationStopwatch.ElapsedMilliseconds,
                normalizationResult.PdfPageCount,
                extractedSections.Count,
                normalizationResult.CandidateBlockCount,
                normalizationResult.ConfirmedRepeatedBlockCount,
                normalizationResult.ChangedSectionCount,
                normalizationResult.RemovedCharacterCount,
                normalizationResult.OriginalCharacterCount,
                normalizationResult.NormalizedCharacterCount);

            try
            {
                await _understandingService.AnalyzeAsync(
                    document.Id,
                    normalizationResult.Sections.Select(section =>
                        new DocumentUnderstandingSourceSection(
                            section.SectionIndex,
                            section.Content,
                            section.PageNumber,
                            section.SectionTitle)).ToArray(),
                    force: false,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception understandingException)
            {
                _logger.LogWarning(
                    "Document understanding did not complete for document {DocumentId}; normalized-text chunking and embedding will continue. Exception type: {ExceptionType}. Document content and provider payloads were omitted.",
                    document.Id,
                    understandingException.GetType().FullName);
            }

            var generatedChunks = _chunkGenerator.Generate(
                normalizationResult.Sections.Select(section => new ChunkSourceSection(
                    section.SectionIndex,
                    section.Content,
                    section.PageNumber,
                    section.SectionTitle)).ToArray(),
                cancellationToken);

            var embeddingStopwatch = Stopwatch.StartNew();
            var embeddingResult = await _embeddingService.GenerateEmbeddingsAsync(
                generatedChunks.Select(chunk => chunk.Content).ToArray(),
                cancellationToken);
            EmbeddingResultValidator.Validate(
                embeddingResult,
                generatedChunks.Count,
                _embeddingOptions.EmbeddingModel,
                EmbeddingArchitecture.Dimensions);
            embeddingStopwatch.Stop();

            _logger.LogInformation(
                "Generated and validated {ChunkCount} chunk embeddings for document {DocumentId} in {DurationMs} ms using model {EmbeddingModel} with {EmbeddingDimensions} dimensions.",
                generatedChunks.Count,
                document.Id,
                embeddingStopwatch.ElapsedMilliseconds,
                embeddingResult.Model,
                embeddingResult.Dimensions);

            await using var transaction = await BeginTransactionIfSupportedAsync(
                cancellationToken);

            await DeleteExistingSectionsAsync(document.Id, cancellationToken);
            await DeleteExistingChunksAsync(document.Id, cancellationToken);

            var completedAtUtc = DateTime.UtcNow;
            var normalizedByIndex = normalizationResult.Sections.ToDictionary(
                section => section.SectionIndex);
            var storedSections = extractedSections
                .OrderBy(section => section.SectionIndex)
                .Select((section, index) => new DocumentTextSection
                {
                    Id = Guid.NewGuid(),
                    DocumentId = document.Id,
                    SectionIndex = index,
                    Content = section.Content,
                    NormalizedContent = normalizedByIndex[section.SectionIndex].Content,
                    NormalizationChanged = normalizedByIndex[section.SectionIndex].Changed,
                    RemovedCharacterCount = normalizedByIndex[section.SectionIndex].RemovedCharacterCount,
                    NormalizedAtUtc = completedAtUtc,
                    PageNumber = section.PageNumber,
                    SectionTitle = Truncate(section.SectionTitle, 500),
                    CreatedAtUtc = completedAtUtc
                })
                .ToArray();

            _dbContext.DocumentTextSections.AddRange(storedSections);

            var chunks = generatedChunks.Select(chunk => new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
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
            document.ProcessedAtUtc = completedAtUtc;
            document.ProcessingError = null;
            document.ExtractedSectionCount = storedSections.Length;
            document.ExtractedCharacterCount = storedSections.Sum(
                section => (long)section.Content.Length);
            document.NormalizedCharacterCount = normalizationResult.NormalizedCharacterCount;
            document.NormalizationRemovedCharacterCount = normalizationResult.RemovedCharacterCount;
            document.NormalizationChangedSectionCount = normalizationResult.ChangedSectionCount;
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
                "Document {DocumentId} processing completed with status Ready using {ExtractorType} in {ElapsedMilliseconds} ms. Extracted {SectionCount} sections and {CharacterCount} characters, normalized to {NormalizedCharacterCount} characters, generated {ChunkCount} chunks, and persisted {EmbeddedChunkCount} embeddings using model {EmbeddingModel} with {EmbeddingDimensions} dimensions.",
                document.Id,
                extractor.GetType().Name,
                stopwatch.ElapsedMilliseconds,
                document.ExtractedSectionCount,
                document.ExtractedCharacterCount,
                document.NormalizedCharacterCount,
                document.ChunkCount,
                document.EmbeddedChunkCount,
                document.EmbeddingModel,
                document.EmbeddingDimensions);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();

            if (exception is DocumentExtractionException)
            {
                try
                {
                    await _understandingService.AnalyzeAsync(
                        document.Id,
                        [],
                        force: false,
                        CancellationToken.None);
                }
                catch (Exception understandingException)
                {
                    _logger.LogWarning(
                        "Could not persist Skipped document-understanding state after extraction produced no normalized source for document {DocumentId}. Exception type: {ExceptionType}.",
                        document.Id,
                        understandingException.GetType().FullName);
                }
            }

            var safeMessage = exception switch
            {
                DocumentChunkingException chunkingException => chunkingException.SafeMessage,
                DocumentNormalizationException normalizationException => normalizationException.SafeMessage,
                DocumentEmbeddingException embeddingException => embeddingException.SafeMessage,
                DocumentExtractionException extractionException => extractionException.SafeMessage,
                OperationCanceledException => "Processing was interrupted. Please retry.",
                _ => "Document processing failed. Please retry."
            };

            try
            {
                await MarkProcessingFailedAsync(
                    document.Id,
                    safeMessage,
                    exception is DocumentNormalizationException,
                    exception is DocumentChunkingException,
                    exception is DocumentEmbeddingException);
            }
            catch (Exception failureUpdateException)
            {
                _logger.LogError(
                    failureUpdateException,
                    "Could not persist Failed status for document {DocumentId}.",
                    document.Id);
            }

            _logger.LogError(
                exception,
                "Document {DocumentId} in project {ProjectId} failed processing with {ExtractorType} after {ElapsedMilliseconds} ms.",
                document.Id,
                document.ProjectId,
                extractor?.GetType().Name ?? "None",
                stopwatch.ElapsedMilliseconds);

            throw;
        }
    }

    private async Task MarkProcessingFailedAsync(
        Guid documentId,
        string safeMessage,
        bool normalizationFailed,
        bool chunkingFailed,
        bool embeddingFailed)
    {
        _dbContext.ChangeTracker.Clear();

        await using var transaction = await BeginTransactionIfSupportedAsync(
            CancellationToken.None);

        var document = await _dbContext.Documents
            .SingleOrDefaultAsync(item => item.Id == documentId);

        if (document is null)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync();
            }

            return;
        }

        await DeleteExistingSectionsAsync(documentId, CancellationToken.None);
        await DeleteExistingChunksAsync(documentId, CancellationToken.None);

        document.Status = DocumentStatus.Failed;
        document.ProcessedAtUtc = null;
        document.ProcessingError = normalizationFailed || chunkingFailed || embeddingFailed
            ? null
            : Truncate(safeMessage, MaximumErrorLength);
        document.ExtractedCharacterCount = 0;
        document.ExtractedSectionCount = 0;
        document.NormalizedCharacterCount = 0;
        document.NormalizationRemovedCharacterCount = 0;
        document.NormalizationChangedSectionCount = 0;
        document.NormalizedAtUtc = null;
        document.NormalizationError = normalizationFailed
            ? Truncate(safeMessage, MaximumErrorLength)
            : null;
        document.ChunkCount = 0;
        document.ChunkedAtUtc = null;
        document.ChunkingError = chunkingFailed
            ? Truncate(safeMessage, MaximumErrorLength)
            : null;
        EmbeddingPersistence.ClearDocumentMetadata(document);
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

    private async Task DeleteExistingSectionsAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.DocumentTextSections
            .Where(section => section.DocumentId == documentId);

        if (_dbContext.Database.IsRelational())
        {
            await query.ExecuteDeleteAsync(cancellationToken);
            return;
        }

        _dbContext.DocumentTextSections.RemoveRange(
            await query.ToListAsync(cancellationToken));
    }

    private async Task DeleteExistingChunksAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.DocumentChunks
            .Where(chunk => chunk.DocumentId == documentId);

        if (_dbContext.Database.IsRelational())
        {
            await query.ExecuteDeleteAsync(cancellationToken);
            return;
        }

        _dbContext.DocumentChunks.RemoveRange(
            await query.ToListAsync(cancellationToken));
    }

    private Task<IDbContextTransaction?> BeginTransactionIfSupportedAsync(
        CancellationToken cancellationToken) =>
        _dbContext.Database.IsRelational()
            ? BeginRelationalTransactionAsync(cancellationToken)
            : Task.FromResult<IDbContextTransaction?>(null);

    private async Task<IDbContextTransaction?> BeginRelationalTransactionAsync(
        CancellationToken cancellationToken) =>
        await _dbContext.Database.BeginTransactionAsync(cancellationToken);

    private static string? Truncate(string? value, int maximumLength) =>
        value is null || value.Length <= maximumLength
            ? value
            : value[..maximumLength];
}
