namespace AI.DocumentAssistant.Server.Models;

public sealed class DocumentUnderstanding
{
    public Guid DocumentId { get; set; }

    public Document Document { get; set; } = null!;

    public DocumentUnderstandingStatus Status { get; set; }

    public DocumentType? DocumentType { get; set; }

    public string? DocumentSubtype { get; set; }

    public double? DocumentTypeConfidence { get; set; }

    public string? PrimaryLanguageCode { get; set; }

    public double? LanguageConfidence { get; set; }

    public string? DetectedTitle { get; set; }

    public string? Subject { get; set; }

    public string? Model { get; set; }

    public string? PromptVersion { get; set; }

    public string? SourceContentHash { get; set; }

    public DateTime? AnalyzedAtUtc { get; set; }

    public string? LastError { get; set; }

    public ICollection<DocumentMetadataEntry> MetadataEntries { get; } =
        new List<DocumentMetadataEntry>();
}
