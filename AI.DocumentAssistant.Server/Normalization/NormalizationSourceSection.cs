namespace AI.DocumentAssistant.Server.Normalization;

public sealed record NormalizationSourceSection(
    int SectionIndex,
    string Content,
    int? PageNumber = null,
    string? SectionTitle = null);
