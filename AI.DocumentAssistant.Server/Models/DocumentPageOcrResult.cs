namespace AI.DocumentAssistant.Server.Models;

public sealed class DocumentPageOcrResult
{
    public Guid DocumentOcrAnalysisId { get; set; }

    public DocumentOcrAnalysis DocumentOcrAnalysis { get; set; } = null!;

    public int PageNumber { get; set; }

    public DocumentPageOcrStatus Status { get; set; }

    public TechnicalType SourceTechnicalType { get; set; }

    public int RecognizedCharacterCount { get; set; }

    public int RecognizedWordCount { get; set; }

    public double? MeanConfidence { get; set; }

    public int? EffectiveRenderDpi { get; set; }

    public int? RenderedWidthPixels { get; set; }

    public int? RenderedHeightPixels { get; set; }

    public long? ProcessingDurationMs { get; set; }

    public bool UsedInExtraction { get; set; }

    public string? LastError { get; set; }
}
