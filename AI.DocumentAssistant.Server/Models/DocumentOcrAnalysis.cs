namespace AI.DocumentAssistant.Server.Models;

public sealed class DocumentOcrAnalysis
{
    public Guid DocumentId { get; set; }

    public Document Document { get; set; } = null!;

    public DocumentOcrStatus Status { get; set; }

    public int CandidatePageCount { get; set; }

    public int SuccessfulPageCount { get; set; }

    public int FailedPageCount { get; set; }

    public string? EngineName { get; set; }

    public string? EngineVersion { get; set; }

    public string? Languages { get; set; }

    public int? RenderDpi { get; set; }

    public int? MaxCandidatePages { get; set; }

    public long? MaxRenderedPixels { get; set; }

    public string? SourceFileHash { get; set; }

    public string? RoutingVersion { get; set; }

    public string? RoutingHash { get; set; }

    public string? ConfigurationHash { get; set; }

    public DateTime? ProcessedAtUtc { get; set; }

    public string? LastError { get; set; }

    public ICollection<DocumentPageOcrResult> Pages { get; } =
        new List<DocumentPageOcrResult>();
}
