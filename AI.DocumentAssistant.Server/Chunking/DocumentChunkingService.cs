using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Embeddings;
using AI.DocumentAssistant.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Chunking;

public sealed class DocumentChunkingService : IDocumentChunkingService
{
    private const int MaximumErrorLength = 500;

    private readonly ApplicationDbContext _dbContext;
    private readonly IDocumentChunkGenerator _generator;
    private readonly ITextEmbeddingService _embeddingService;
    private readonly OpenAIEmbeddingOptions _embeddingOptions;
    private readonly ILogger<DocumentChunkingService> _logger;

    public DocumentChunkingService(
        ApplicationDbContext dbContext,
        IDocumentChunkGenerator generator,
        ITextEmbeddingService embeddingService,
        IOptions<OpenAIEmbeddingOptions> embeddingOptions,
        ILogger<DocumentChunkingService> logger)
    {
        _dbContext = dbContext;
        _generator = generator;
        _embeddingService = embeddingService;
        _embeddingOptions = embeddingOptions.Value;
        _logger = logger;
    }

    public async Task<DocumentChunkingResult> RebuildAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        Document? document = null;

        try
        {
            document = await _dbContext.Documents
                .SingleOrDefaultAsync(item => item.Id == documentId, cancellationToken)
                ?? throw new DocumentChunkingException(
                    "The document is not available for chunking.");

            var sourceSections = await _dbContext.DocumentTextSections
                .AsNoTracking()
                .Where(section => section.DocumentId == documentId)
                .OrderBy(section => section.SectionIndex)
                .Select(section => new ChunkSourceSection(
                    section.SectionIndex,
                    section.NormalizedContent ?? section.Content,
                    section.PageNumber,
                    section.SectionTitle))
                .ToListAsync(cancellationToken);

            var generatedChunks = _generator.Generate(
                sourceSections,
                cancellationToken);

            var embeddingResult = await _embeddingService.GenerateEmbeddingsAsync(
                generatedChunks.Select(chunk => chunk.Content).ToArray(),
                cancellationToken);
            EmbeddingResultValidator.Validate(
                embeddingResult,
                generatedChunks.Count,
                _embeddingOptions.EmbeddingModel,
                EmbeddingArchitecture.Dimensions);

            await using var transaction = await BeginTransactionIfSupportedAsync(
                cancellationToken);

            await DeleteExistingChunksAsync(documentId, cancellationToken);

            var completedAtUtc = DateTime.UtcNow;
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

            _logger.LogInformation(
                "Generated and embedded {ChunkCount} chunks for document {DocumentId} in project {ProjectId} using model {EmbeddingModel} with {EmbeddingDimensions} dimensions.",
                chunks.Length,
                document.Id,
                document.ProjectId,
                embeddingResult.Model,
                embeddingResult.Dimensions);

            return new DocumentChunkingResult(chunks.Length, completedAtUtc);
        }
        catch (Exception exception)
        {
            var safeMessage = exception switch
            {
                DocumentChunkingException chunkingException => chunkingException.SafeMessage,
                DocumentEmbeddingException embeddingException => embeddingException.SafeMessage,
                OperationCanceledException => "Chunk generation was interrupted. Please retry.",
                _ => "Document chunk generation failed. Please retry."
            };

            try
            {
                await RestoreReadyAfterFailureAsync(
                    documentId,
                    safeMessage,
                    exception is DocumentEmbeddingException);
            }
            catch (Exception failureUpdateException)
            {
                _logger.LogError(
                    failureUpdateException,
                    "Could not persist chunking failure for document {DocumentId}.",
                    documentId);
            }

            _logger.LogError(
                exception,
                "Chunk rebuild failed for document {DocumentId} in project {ProjectId}; the previous authoritative chunks and embeddings were preserved.",
                documentId,
                document?.ProjectId);

            if (exception is OperationCanceledException)
            {
                throw;
            }

            throw exception is DocumentEmbeddingException preservedEmbeddingException
                ? preservedEmbeddingException
                : new DocumentChunkingException(safeMessage, exception);
        }
    }

    private async Task RestoreReadyAfterFailureAsync(
        Guid documentId,
        string safeMessage,
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

        document.Status = DocumentStatus.Ready;
        document.ChunkingError = embeddingFailed
            ? null
            : Truncate(safeMessage, MaximumErrorLength);
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
