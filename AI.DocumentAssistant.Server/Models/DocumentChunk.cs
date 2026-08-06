namespace AI.DocumentAssistant.Server.Models;

public sealed class DocumentChunk
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public Document Document { get; set; } = null!;

    public int ChunkIndex { get; set; }

    public string Content { get; set; } = string.Empty;

    public int CharacterCount { get; set; }

    public int TokenCount { get; set; }

    public int? PageStart { get; set; }

    public int? PageEnd { get; set; }

    public string? SectionTitle { get; set; }

    public int SourceSectionStartIndex { get; set; }

    public int SourceSectionEndIndex { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
