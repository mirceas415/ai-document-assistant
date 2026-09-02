namespace AI.DocumentAssistant.Server.TechnicalAnalysis;

/// <summary>
/// Versioned deterministic thresholds for <c>pdf-technical-analysis-v1</c>.
/// Changing their meaning requires a new analyzer version so persisted results
/// are not silently reused under different rules.
/// </summary>
public static class PdfTechnicalAnalysisHeuristics
{
    public const int MinimumAlphanumericCharacterCount = 40;
    public const int MinimumUsefulWordCount = 8;
    public const int MinimumUsefulWordAlphanumericLength = 2;
    public const double SubstantialImageCoverageThreshold = 0.30;
    public const double PageSizedImageCoverageThreshold = 0.80;
    public const double DocumentStrongMajorityThreshold = 0.80;
    public const double MaximumUnknownPageRatio = 0.20;
    public const int MinimumUnknownPageTolerance = 1;
}
