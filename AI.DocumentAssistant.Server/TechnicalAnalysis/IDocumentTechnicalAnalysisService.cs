using AI.DocumentAssistant.Server.Models;

namespace AI.DocumentAssistant.Server.TechnicalAnalysis;

public interface IDocumentTechnicalAnalysisService
{
    Task<DocumentTechnicalAnalysisRunResult> AnalyzeAsync(
        Guid documentId,
        bool force,
        CancellationToken cancellationToken);
}

public sealed record DocumentTechnicalAnalysisRunResult(
    DocumentTechnicalAnalysisStatus Status,
    TechnicalType TechnicalType,
    string? SourceFileHash,
    string? AnalyzerVersion,
    bool Reused);
