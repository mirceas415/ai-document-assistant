namespace AI.DocumentAssistant.Server.Understanding;

public sealed record DocumentUnderstandingSourceSection(
    int SectionIndex,
    string NormalizedContent,
    int? PageNumber,
    string? SectionTitle);
