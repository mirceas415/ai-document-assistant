namespace AI.DocumentAssistant.Server.Retrieval;

public sealed class PassThroughRetrievalReranker : IRetrievalReranker
{
    public Task<RetrievalRerankerResult> RerankAsync(
        RetrievalRerankingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new RetrievalRerankerResult(
            request.Candidates
                .Select(candidate => new RetrievalRerankerRank(
                    candidate.CandidateId,
                    2))
                .ToArray()));
    }
}
