using System.ComponentModel.DataAnnotations;

namespace AI.DocumentAssistant.Server.Retrieval;

public sealed class HybridRetrievalOptions
{
    public const string SectionName = "HybridRetrieval";

    [Range(1, SemanticRetrievalLimits.MaximumCandidateCount)]
    public int VectorCandidateCount { get; init; } =
        SemanticRetrievalLimits.DefaultVectorCandidateCount;

    [Range(1, SemanticRetrievalLimits.MaximumCandidateCount)]
    public int LexicalCandidateCount { get; init; } =
        SemanticRetrievalLimits.DefaultLexicalCandidateCount;

    [Range(1, SemanticRetrievalLimits.MaximumMetadataDocumentCandidateCount)]
    public int MetadataDocumentCandidateCount { get; init; } =
        SemanticRetrievalLimits.DefaultMetadataDocumentCandidateCount;

    [Range(1, 1_000)]
    public int ReciprocalRankConstant { get; init; } = 60;

    [Range(0.01, 10)]
    public double VectorWeight { get; init; } = 1.0;

    [Range(0.01, 10)]
    public double LexicalWeight { get; init; } = 1.0;

    [Range(0.01, 1)]
    public double MetadataWeight { get; init; } = 0.35;

    [Range(1, 2)]
    public double ExactIdentifierMultiplier { get; init; } = 1.5;
}
