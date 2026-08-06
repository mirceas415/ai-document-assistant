namespace AI.DocumentAssistant.Server.Chunking;

public sealed record GeneratedDocumentChunk(
    int ChunkIndex,
    string Content,
    int CharacterCount,
    int TokenCount,
    int? PageStart,
    int? PageEnd,
    string? SectionTitle,
    int SourceSectionStartIndex,
    int SourceSectionEndIndex);
