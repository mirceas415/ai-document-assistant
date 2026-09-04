using AI.DocumentAssistant.Server.Models;

namespace AI.DocumentAssistant.Server.Contracts;

public sealed record DocumentOcrAnalysisResponse(
    DocumentOcrStatus Status,
    int CandidatePageCount,
    int SuccessfulPageCount,
    int FailedPageCount,
    string? EngineName,
    string? EngineVersion,
    string? Languages,
    int? RenderDpi,
    int? MaxCandidatePages,
    long? MaxRenderedPixels,
    string? SourceFileHash,
    string? RoutingVersion,
    string? RoutingHash,
    string? ConfigurationHash,
    DateTime? ProcessedAtUtc,
    string? LastError,
    IReadOnlyList<DocumentPageOcrResultResponse> Pages);

public sealed record DocumentPageOcrResultResponse(
    int PageNumber,
    DocumentPageOcrStatus Status,
    TechnicalType SourceTechnicalType,
    int RecognizedCharacterCount,
    int RecognizedWordCount,
    double? MeanConfidence,
    int? EffectiveRenderDpi,
    int? RenderedWidthPixels,
    int? RenderedHeightPixels,
    long? ProcessingDurationMs,
    bool UsedInExtraction,
    string? LastError);
