using System.ComponentModel.DataAnnotations;

namespace AI.DocumentAssistant.Server.Normalization;

public sealed class DocumentNormalizationOptions
{
    public const string SectionName = "DocumentNormalization";

    [Range(1, 100)]
    public int HeaderCandidateLineCount { get; init; } = 15;

    [Range(1, 100)]
    public int FooterCandidateLineCount { get; init; } = 15;

    [Range(0.01, 1.0)]
    public double MinimumPageOccurrenceRatio { get; init; } = 0.6;

    [Range(3, 10_000)]
    public int MinimumPageCountForBoilerplateDetection { get; init; } = 3;

    [Range(1, 100_000)]
    public int MinimumCandidateBlockLength { get; init; } = 40;

    [Range(1, 100_000)]
    public int MinimumLocalCandidateBlockLength { get; init; } = 160;

    [Range(1, 100_000)]
    public int MaximumCandidateLength { get; init; } = 4_000;

    [Range(0, 10)]
    public int MaximumBlockBoundaryLineOffset { get; init; } = 2;

    public bool EnablePageNumberRemoval { get; init; } = true;

    public bool EnableWordBreakRepair { get; init; } = true;
}
