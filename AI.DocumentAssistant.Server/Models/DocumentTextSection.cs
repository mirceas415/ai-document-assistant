namespace AI.DocumentAssistant.Server.Models;

public sealed class DocumentTextSection
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public Document Document { get; set; } = null!;

    public int SectionIndex { get; set; }

    public string Content { get; set; } = string.Empty;

    public int? PageNumber { get; set; }

    public string? SectionTitle { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
