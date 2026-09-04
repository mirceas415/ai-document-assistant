using AI.DocumentAssistant.Server.Models;

namespace AI.DocumentAssistant.Server.Processing;

public sealed record ExtractedTextSection(
    int SectionIndex,
    string Content,
    int? PageNumber = null,
    string? SectionTitle = null,
    DocumentTextExtractionMethod ExtractionMethod = DocumentTextExtractionMethod.Unknown);
