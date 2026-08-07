namespace AI.DocumentAssistant.Server.Embeddings;

public interface IDocumentEmbeddingService
{
    Task<DocumentEmbeddingRebuildResult> RebuildAsync(
        Guid documentId,
        CancellationToken cancellationToken);
}

public sealed record DocumentEmbeddingRebuildResult(
    int EmbeddedChunkCount,
    string EmbeddingModel,
    int EmbeddingDimensions,
    DateTime EmbeddedAtUtc);
