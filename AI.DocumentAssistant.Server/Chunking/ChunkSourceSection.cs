namespace AI.DocumentAssistant.Server.Chunking;

public sealed record ChunkSourceSection(
    int SectionIndex,
    string Content,
    int? PageNumber = null,
    string? SectionTitle = null);
