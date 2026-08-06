namespace AI.DocumentAssistant.Server.Normalization;

public sealed record NormalizedTextSection(
    int SectionIndex,
    string Content,
    int? PageNumber,
    string? SectionTitle,
    bool Changed,
    int RemovedCharacterCount);

public sealed record DocumentNormalizationResult(
    IReadOnlyList<NormalizedTextSection> Sections,
    long OriginalCharacterCount,
    long NormalizedCharacterCount,
    int ChangedSectionCount,
    long RemovedCharacterCount,
    int PdfPageCount = 0,
    int CandidateBlockCount = 0,
    int ConfirmedRepeatedBlockCount = 0);
