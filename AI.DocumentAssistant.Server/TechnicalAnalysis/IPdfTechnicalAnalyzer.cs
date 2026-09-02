namespace AI.DocumentAssistant.Server.TechnicalAnalysis;

public interface IPdfTechnicalAnalyzer
{
    string AnalyzerVersion { get; }

    Task<PdfTechnicalAnalysisResult> AnalyzeAsync(
        Stream pdfStream,
        CancellationToken cancellationToken);
}
