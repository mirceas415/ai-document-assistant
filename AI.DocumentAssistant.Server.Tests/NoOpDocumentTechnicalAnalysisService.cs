using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.TechnicalAnalysis;

namespace AI.DocumentAssistant.Server.Tests;

internal sealed class NoOpDocumentTechnicalAnalysisService
    : IDocumentTechnicalAnalysisService
{
    public Task<DocumentTechnicalAnalysisRunResult> AnalyzeAsync(
        Guid documentId,
        bool force,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DocumentTechnicalAnalysisRunResult(
            DocumentTechnicalAnalysisStatus.NotAnalyzed,
            TechnicalType.Unknown,
            null,
            null,
            true));
    }
}
