namespace AI.DocumentAssistant.Server.Chunking;

public interface IDocumentChunkingService
{
    Task<DocumentChunkingResult> RebuildAsync(
        Guid documentId,
        CancellationToken cancellationToken);
}

public sealed record DocumentChunkingResult(
    int ChunkCount,
    DateTime ChunkedAtUtc);
