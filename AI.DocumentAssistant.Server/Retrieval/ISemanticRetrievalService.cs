namespace AI.DocumentAssistant.Server.Retrieval;

public interface ISemanticRetrievalService
{
    Task<SemanticRetrievalResult?> SearchAsync(
        Guid ownerId,
        Guid projectId,
        string query,
        int topK,
        CancellationToken cancellationToken);
}

public sealed record SemanticRetrievalResult(
    int TopK,
    IReadOnlyList<RetrievedDocumentChunk> Chunks,
    bool RerankingApplied = false,
    bool RerankingFallback = false);
