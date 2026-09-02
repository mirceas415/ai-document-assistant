using AI.DocumentAssistant.Server.Models;

namespace AI.DocumentAssistant.Server.TechnicalAnalysis;

public sealed record PdfTextMetrics(
    int AlphanumericCharacterCount,
    int UsefulWordCount);

public sealed record PdfPageTechnicalMetrics(
    int PageNumber,
    int TextCharacterCount,
    int WordCount,
    int ImageCount,
    double ImageCoverageRatio);

public sealed record PdfPageTechnicalAnalysisResult(
    int PageNumber,
    TechnicalType TechnicalType,
    int TextCharacterCount,
    int WordCount,
    int ImageCount,
    double ImageCoverageRatio,
    bool HasMeaningfulText,
    bool HasPageSizedImage);

public sealed record PdfTechnicalAnalysisResult(
    TechnicalType TechnicalType,
    IReadOnlyList<PdfPageTechnicalAnalysisResult> Pages);
