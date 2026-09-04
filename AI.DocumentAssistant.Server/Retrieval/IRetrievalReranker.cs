namespace AI.DocumentAssistant.Server.Retrieval;

public interface IRetrievalReranker
{
    Task<RetrievalRerankerResult> RerankAsync(
        RetrievalRerankingRequest request,
        CancellationToken cancellationToken);
}

public sealed record RetrievalRerankingRequest(
    string Question,
    IReadOnlyList<RetrievalRerankingCandidate> Candidates,
    int ApproximateInputTokenCount);

public sealed record RetrievalRerankingCandidate(
    string CandidateId,
    Guid ChunkId,
    string DocumentName,
    string PageLabel,
    string? Heading,
    string Content,
    int ApproximateTokenCount);

public sealed record RetrievalRerankerResult(
    IReadOnlyList<RetrievalRerankerRank>? Ranking);

public sealed record RetrievalRerankerRank(
    string? CandidateId,
    int? Relevance);
