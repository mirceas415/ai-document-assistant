namespace AI.DocumentAssistant.Server.Models;

public sealed class DocumentPageTechnicalAnalysis
{
    public Guid DocumentTechnicalAnalysisId { get; set; }

    public DocumentTechnicalAnalysis DocumentTechnicalAnalysis { get; set; } = null!;

    public int PageNumber { get; set; }

    public TechnicalType TechnicalType { get; set; }

    /// <summary>The number of Unicode letters and digits found in the PDF text layer.</summary>
    public int TextCharacterCount { get; set; }

    /// <summary>The number of useful alphanumeric word runs used by the text heuristic.</summary>
    public int WordCount { get; set; }

    /// <summary>The number of non-mask raster image placements reported by PdfPig.</summary>
    public int ImageCount { get; set; }

    /// <summary>
    /// The visible page-area ratio of the largest raster image. This conservative lower
    /// bound avoids double-counting overlapping image placements.
    /// </summary>
    public double ImageCoverageRatio { get; set; }

    public bool HasMeaningfulText { get; set; }

    public bool HasPageSizedImage { get; set; }
}
