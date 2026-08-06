namespace AI.DocumentAssistant.Server.Models;

public sealed class Document
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Project Project { get; set; } = null!;

    public string OriginalFileName { get; set; } = string.Empty;

    public string StoredFileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public DocumentStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? ProcessingStartedAtUtc { get; set; }

    public DateTime? ProcessedAtUtc { get; set; }

    public string? ProcessingError { get; set; }

    public long ExtractedCharacterCount { get; set; }

    public int ExtractedSectionCount { get; set; }

    public int ChunkCount { get; set; }

    public DateTime? ChunkedAtUtc { get; set; }

    public string? ChunkingError { get; set; }

    public long NormalizedCharacterCount { get; set; }

    public long NormalizationRemovedCharacterCount { get; set; }

    public int NormalizationChangedSectionCount { get; set; }

    public DateTime? NormalizedAtUtc { get; set; }

    public string? NormalizationError { get; set; }

    public ICollection<DocumentTextSection> TextSections { get; } = new List<DocumentTextSection>();

    public ICollection<DocumentChunk> Chunks { get; } = new List<DocumentChunk>();
}
