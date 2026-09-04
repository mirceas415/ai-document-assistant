using System.ComponentModel.DataAnnotations;

namespace AI.DocumentAssistant.Server.Retrieval;

public sealed class RetrievalRerankingOptions
{
    public const string SectionName = "Retrieval:Reranking";

    public bool Enabled { get; init; } = true;

    [Range(2, RetrievalRerankingLimits.MaximumCandidateCount)]
    public int CandidateCount { get; init; } =
        RetrievalRerankingLimits.DefaultCandidateCount;

    [Range(2, RetrievalRerankingLimits.MaximumCandidateCount)]
    public int MaxCandidateCount { get; init; } =
        RetrievalRerankingLimits.MaximumCandidateCount;

    [Range(
        RetrievalRerankingLimits.MinimumMaxInputTokens,
        RetrievalRerankingLimits.MaximumMaxInputTokens)]
    public int MaxInputTokens { get; init; } =
        RetrievalRerankingLimits.DefaultMaxInputTokens;

    [Range(
        RetrievalRerankingLimits.MinimumMaxCandidateTokens,
        RetrievalRerankingLimits.MaximumMaxCandidateTokens)]
    public int MaxCandidateTokens { get; init; } =
        RetrievalRerankingLimits.DefaultMaxCandidateTokens;

    [Range(1, RetrievalRerankingLimits.MaximumTimeoutSeconds)]
    public int TimeoutSeconds { get; init; } =
        RetrievalRerankingLimits.DefaultTimeoutSeconds;
}
