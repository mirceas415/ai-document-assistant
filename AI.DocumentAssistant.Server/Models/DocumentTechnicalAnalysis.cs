namespace AI.DocumentAssistant.Server.Models;

public sealed class DocumentTechnicalAnalysis
{
    public Guid DocumentId { get; set; }

    public Document Document { get; set; } = null!;

    public DocumentTechnicalAnalysisStatus Status { get; set; }

    public TechnicalType TechnicalType { get; set; }

    public int PageCount { get; set; }

    public int TextBasedPageCount { get; set; }

    public int ScannedPageCount { get; set; }

    public int ImageBasedPageCount { get; set; }

    public int MixedPageCount { get; set; }

    public int UnknownPageCount { get; set; }

    public string? SourceFileHash { get; set; }

    public string? AnalyzerVersion { get; set; }

    public DateTime? AnalyzedAtUtc { get; set; }

    public string? LastError { get; set; }

    public ICollection<DocumentPageTechnicalAnalysis> Pages { get; } =
        new List<DocumentPageTechnicalAnalysis>();
}
