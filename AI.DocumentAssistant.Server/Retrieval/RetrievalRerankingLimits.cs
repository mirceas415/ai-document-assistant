namespace AI.DocumentAssistant.Server.Retrieval;

public static class RetrievalRerankingLimits
{
    public const int DefaultCandidateCount = 18;

    public const int MaximumCandidateCount = 30;

    public const int DefaultMaxInputTokens = 12_000;

    public const int MinimumMaxInputTokens = 1_000;

    public const int MaximumMaxInputTokens = 20_000;

    public const int DefaultMaxCandidateTokens = 700;

    public const int MinimumMaxCandidateTokens = 100;

    public const int MaximumMaxCandidateTokens = 2_000;

    public const int DefaultTimeoutSeconds = 30;

    public const int MaximumTimeoutSeconds = 60;

    public const int MaximumOutputTokens = 800;

    public const int MinimumRelevance = 0;

    public const int MaximumRelevance = 4;
}
