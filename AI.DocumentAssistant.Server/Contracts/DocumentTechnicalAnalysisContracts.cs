using AI.DocumentAssistant.Server.Models;

namespace AI.DocumentAssistant.Server.Contracts;

public sealed record DocumentTechnicalAnalysisResponse(
    DocumentTechnicalAnalysisStatus Status,
    TechnicalType TechnicalType,
    int PageCount,
    int TextBasedPageCount,
    int ScannedPageCount,
    int ImageBasedPageCount,
    int MixedPageCount,
    int UnknownPageCount,
    string? SourceFileHash,
    string? AnalyzerVersion,
    DateTime? AnalyzedAtUtc,
    string? LastError,
    IReadOnlyList<DocumentPageTechnicalAnalysisResponse> Pages);

public sealed record DocumentPageTechnicalAnalysisResponse(
    int PageNumber,
    TechnicalType TechnicalType,
    int TextCharacterCount,
    int WordCount,
    int ImageCount,
    double ImageCoverageRatio,
    bool HasMeaningfulText,
    bool HasPageSizedImage);
