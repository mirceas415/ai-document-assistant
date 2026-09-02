namespace AI.DocumentAssistant.Server.Models;

public sealed class DocumentMetadataEntry
{
    public Guid Id { get; set; }

    public Guid DocumentUnderstandingId { get; set; }

    public DocumentUnderstanding DocumentUnderstanding { get; set; } = null!;

    public DocumentMetadataKind Kind { get; set; }

    public string Label { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string? NormalizedValue { get; set; }

    public double? Confidence { get; set; }

    public int Sequence { get; set; }
}
