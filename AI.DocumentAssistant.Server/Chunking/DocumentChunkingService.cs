using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AI.DocumentAssistant.Server.Chunking;

public sealed class DocumentChunkingService : IDocumentChunkingService
{
    private const int MaximumErrorLength = 500;

    private readonly ApplicationDbContext _dbContext;
    private readonly IDocumentChunkGenerator _generator;
    private readonly ILogger<DocumentChunkingService> _logger;

    public DocumentChunkingService(
        ApplicationDbContext dbContext,
        IDocumentChunkGenerator generator,
        ILogger<DocumentChunkingService> logger)
    {
        _dbContext = dbContext;
        _generator = generator;
        _logger = logger;
    }

    public async Task<DocumentChunkingResult> RebuildAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var document = await _dbContext.Documents
            .SingleOrDefaultAsync(item => item.Id == documentId, cancellationToken)
            ?? throw new DocumentChunkingException(
                "The document is not available for chunking.");

        try
        {
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

            _dbContext.DocumentChunks.AddRange(chunks);

            document.Status = DocumentStatus.Ready;
            document.ProcessedAtUtc ??= completedAtUtc;
            document.ProcessingError = null;
            document.ChunkCount = chunks.Length;
            document.ChunkedAtUtc = completedAtUtc;
            document.ChunkingError = null;
            document.UpdatedAtUtc = completedAtUtc;

            await _dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            _logger.LogInformation(
                "Generated {ChunkCount} chunks for document {DocumentId} in project {ProjectId}.",
                chunks.Length,
                document.Id,
                document.ProjectId);

            return new DocumentChunkingResult(chunks.Length, completedAtUtc);
        }
        catch (Exception exception)
        {
            var safeMessage = exception switch
            {
                DocumentChunkingException chunkingException => chunkingException.SafeMessage,
                OperationCanceledException => "Chunk generation was interrupted. Please retry.",
                _ => "Document chunk generation failed. Please retry."
            };

            try
            {
                await MarkChunkingFailedAsync(documentId, safeMessage);
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
                "Chunk generation failed for document {DocumentId} in project {ProjectId}.",
                document.Id,
                document.ProjectId);

            if (exception is OperationCanceledException)
            {
                throw;
            }

            throw new DocumentChunkingException(safeMessage, exception);
        }
    }

    private async Task MarkChunkingFailedAsync(Guid documentId, string safeMessage)
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

        await DeleteExistingChunksAsync(documentId, CancellationToken.None);

        document.Status = DocumentStatus.Failed;
        document.ProcessedAtUtc = null;
        document.ProcessingError = null;
        document.ChunkCount = 0;
        document.ChunkedAtUtc = null;
        document.ChunkingError = Truncate(safeMessage, MaximumErrorLength);
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
