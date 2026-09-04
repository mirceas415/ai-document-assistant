namespace AI.DocumentAssistant.Server.Retrieval;

public static class SemanticRetrievalLimits
{
    public const int DefaultTopK = 8;

    public const int MaximumTopK = 20;

    public const int MaximumQueryLength = 2_000;

    public const int DefaultVectorCandidateCount = 30;

    public const int DefaultLexicalCandidateCount = 30;

    public const int DefaultMetadataDocumentCandidateCount = 20;

    public const int MaximumCandidateCount = 100;

    public const int MaximumMetadataDocumentCandidateCount = 50;

    public const int MaximumMatchedMetadataSignals = 3;
}
