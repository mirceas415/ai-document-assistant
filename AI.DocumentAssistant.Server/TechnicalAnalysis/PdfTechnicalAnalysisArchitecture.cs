namespace AI.DocumentAssistant.Server.TechnicalAnalysis;

public static class PdfTechnicalAnalysisArchitecture
{
    public const string PdfContentType = "application/pdf";
    public const string AnalyzerVersion = "pdf-technical-analysis-v1";
    public const int SourceFileHashLength = 64;
    public const int MaximumAnalyzerVersionLength = 64;
    public const int MaximumErrorLength = 500;
    public const string SafeFailureMessage =
        "Technical PDF analysis could not be completed. Please retry.";
    public const string NotApplicableMessage =
        "Technical PDF analysis is not applicable to DOCX documents.";

    public static bool IsPdf(string contentType) =>
        string.Equals(contentType, PdfContentType, StringComparison.OrdinalIgnoreCase);
}
