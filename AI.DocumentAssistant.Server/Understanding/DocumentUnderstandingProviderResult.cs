namespace AI.DocumentAssistant.Server.Understanding;

public sealed record DocumentUnderstandingProviderResult(
    string? DocumentType,
    string? DocumentSubtype,
    double? DocumentTypeConfidence,
    string? PrimaryLanguageCode,
    double? LanguageConfidence,
    string? DetectedTitle,
    string? Subject,
    IReadOnlyList<DocumentUnderstandingProviderMetadataEntry>? Metadata);

public sealed record DocumentUnderstandingProviderMetadataEntry(
    string? Kind,
    string? Label,
    string? Value,
    double? Confidence);
