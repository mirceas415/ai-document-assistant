using System.Diagnostics;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Embeddings;

public sealed class DocumentEmbeddingService : IDocumentEmbeddingService
{
    private const int MaximumErrorLength = 500;

    private readonly ApplicationDbContext _dbContext;
    private readonly ITextEmbeddingService _embeddingService;
    private readonly OpenAIEmbeddingOptions _options;
    private readonly ILogger<DocumentEmbeddingService> _logger;

    public DocumentEmbeddingService(
        ApplicationDbContext dbContext,
        ITextEmbeddingService embeddingService,
        IOptions<OpenAIEmbeddingOptions> options,
        ILogger<DocumentEmbeddingService> logger)
    {
        _dbContext = dbContext;
        _embeddingService = embeddingService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DocumentEmbeddingRebuildResult> RebuildAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        Guid? projectId = null;

        try
        {
            var documentInfo = await _dbContext.Documents
                .AsNoTracking()
                .Where(document => document.Id == documentId)
                .Select(document => new { document.Id, document.ProjectId })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new DocumentEmbeddingException(
                    "The document is not available for embedding.");
            projectId = documentInfo.ProjectId;

            var snapshot = await _dbContext.DocumentChunks
                .AsNoTracking()
                .Where(chunk => chunk.DocumentId == documentId)
                .OrderBy(chunk => chunk.ChunkIndex)
                .Select(chunk => new ChunkSnapshot(
                    chunk.Id,
                    chunk.ChunkIndex,
                    chunk.Content))
                .ToListAsync(cancellationToken);

            if (snapshot.Count == 0)
            {
                throw new DocumentEmbeddingException(
                    "Document chunks are required before embeddings can be generated.");
            }

            _logger.LogInformation(
                "Rebuilding embeddings for document {DocumentId} in project {ProjectId} from {ChunkCount} persisted chunks using model {EmbeddingModel} with {EmbeddingDimensions} dimensions.",
                documentInfo.Id,
                documentInfo.ProjectId,
                snapshot.Count,
                _options.EmbeddingModel,
                _options.EmbeddingDimensions);

            var result = await _embeddingService.GenerateEmbeddingsAsync(
                snapshot.Select(chunk => chunk.Content).ToArray(),
                cancellationToken);
            EmbeddingResultValidator.Validate(
                result,
                snapshot.Count,
                _options.EmbeddingModel,
                EmbeddingArchitecture.Dimensions);

            await using var transaction = await BeginTransactionIfSupportedAsync(
                cancellationToken);

            _dbContext.ChangeTracker.Clear();
            var document = await _dbContext.Documents
                .SingleOrDefaultAsync(item => item.Id == documentId, cancellationToken)
                ?? throw new DocumentEmbeddingException(
                    "The document changed while embeddings were being generated. Please retry.");
            var chunks = await _dbContext.DocumentChunks
                .Where(chunk => chunk.DocumentId == documentId)
                .OrderBy(chunk => chunk.ChunkIndex)
                .ToListAsync(cancellationToken);

            EnsureUnchanged(snapshot, chunks);

            var completedAtUtc = DateTime.UtcNow;
            for (var index = 0; index < chunks.Count; index++)
            {
                EmbeddingPersistence.ApplyToChunk(
                    chunks[index],
                    result.Embeddings[index],
                    result,
                    completedAtUtc);
            }

            EmbeddingPersistence.ApplyToDocument(
                document,
                chunks.Count,
                result,
                completedAtUtc);
            document.Status = DocumentStatus.Ready;
            document.ProcessedAtUtc ??= completedAtUtc;
            document.UpdatedAtUtc = completedAtUtc;

            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            stopwatch.Stop();
            _logger.LogInformation(
                "Embedding rebuild completed for document {DocumentId} in project {ProjectId} with {ChunkCount} chunks in {DurationMs} ms using model {EmbeddingModel} with {EmbeddingDimensions} dimensions.",
                document.Id,
                document.ProjectId,
                chunks.Count,
                stopwatch.ElapsedMilliseconds,
                result.Model,
                result.Dimensions);

            return new DocumentEmbeddingRebuildResult(
                chunks.Count,
                result.Model,
                result.Dimensions,
                completedAtUtc);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            var safeMessage = exception switch
            {
                DocumentEmbeddingException embeddingException => embeddingException.SafeMessage,
                OperationCanceledException => "Embedding generation was interrupted. Please retry.",
                _ => "Document embeddings could not be generated. Please try again."
            };

            try
            {
                await RestoreReadyWithErrorAsync(documentId, safeMessage);
            }
            catch (Exception failureUpdateException)
            {
                _logger.LogError(
                    failureUpdateException,
                    "Could not restore document {DocumentId} after an embedding rebuild failure.",
                    documentId);
            }

            _logger.LogError(
                exception,
                "Embedding rebuild failed for document {DocumentId} in project {ProjectId} after {DurationMs} ms; the previous authoritative chunks and embeddings were preserved.",
                documentId,
                projectId,
                stopwatch.ElapsedMilliseconds);

            if (exception is OperationCanceledException)
            {
                throw;
            }

            if (exception is DocumentEmbeddingException)
            {
                throw;
            }

            throw new DocumentEmbeddingException(safeMessage, exception);
        }
    }

    private static void EnsureUnchanged(
        IReadOnlyList<ChunkSnapshot> snapshot,
        IReadOnlyList<DocumentChunk> chunks)
    {
        if (snapshot.Count != chunks.Count)
        {
            throw new DocumentEmbeddingException(
                "The document changed while embeddings were being generated. Please retry.");
        }

        for (var index = 0; index < snapshot.Count; index++)
        {
            if (snapshot[index].Id != chunks[index].Id ||
                snapshot[index].ChunkIndex != chunks[index].ChunkIndex ||
                !string.Equals(snapshot[index].Content, chunks[index].Content, StringComparison.Ordinal))
            {
                throw new DocumentEmbeddingException(
                    "The document changed while embeddings were being generated. Please retry.");
            }
        }
    }

    private async Task RestoreReadyWithErrorAsync(Guid documentId, string safeMessage)
    {
        _dbContext.ChangeTracker.Clear();
        var document = await _dbContext.Documents
            .SingleOrDefaultAsync(item => item.Id == documentId);
        if (document is null)
        {
            return;
        }

        document.Status = DocumentStatus.Ready;
        document.EmbeddingError = Truncate(safeMessage, MaximumErrorLength);
        document.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }

    private Task<IDbContextTransaction?> BeginTransactionIfSupportedAsync(
        CancellationToken cancellationToken) =>
        _dbContext.Database.IsRelational()
            ? BeginRelationalTransactionAsync(cancellationToken)
            : Task.FromResult<IDbContextTransaction?>(null);

    private async Task<IDbContextTransaction?> BeginRelationalTransactionAsync(
        CancellationToken cancellationToken) =>
        await _dbContext.Database.BeginTransactionAsync(cancellationToken);

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

    private sealed record ChunkSnapshot(Guid Id, int ChunkIndex, string Content);
}
