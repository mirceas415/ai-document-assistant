using AI.DocumentAssistant.Server.Models;

namespace AI.DocumentAssistant.Server.Contracts;

public sealed record DocumentUnderstandingResponse(
    DocumentUnderstandingStatus Status,
    DocumentType? DocumentType,
    string? DocumentSubtype,
    double? DocumentTypeConfidence,
    string? PrimaryLanguageCode,
    double? LanguageConfidence,
    string? DetectedTitle,
    string? Subject,
    IReadOnlyList<DocumentMetadataEntryResponse> Metadata,
    string? Model,
    string? PromptVersion,
    string? SourceContentHash,
    DateTime? AnalyzedAtUtc,
    string? LastError);

public sealed record DocumentMetadataEntryResponse(
    DocumentMetadataKind Kind,
    string Label,
    string Value,
    string? NormalizedValue,
    double? Confidence,
    int Sequence);
