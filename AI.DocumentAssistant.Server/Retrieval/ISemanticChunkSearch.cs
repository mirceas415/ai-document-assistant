using Pgvector;

namespace AI.DocumentAssistant.Server.Retrieval;

public interface ISemanticChunkSearch
{
    Task<IReadOnlyList<RetrievedDocumentChunk>> SearchAsync(
        Guid ownerId,
        Guid projectId,
        Vector queryEmbedding,
        string embeddingModel,
        int embeddingDimensions,
        int topK,
        CancellationToken cancellationToken);
}

public sealed record RetrievedDocumentChunk(
    Guid DocumentId,
    string DocumentName,
    Guid ChunkId,
    int ChunkIndex,
    string Content,
    int? PageStart,
    int? PageEnd,
    string? Heading,
    double CosineDistance);
