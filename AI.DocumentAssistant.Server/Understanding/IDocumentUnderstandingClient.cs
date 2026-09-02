namespace AI.DocumentAssistant.Server.Understanding;

public interface IDocumentUnderstandingClient
{
    Task<DocumentUnderstandingProviderResult> AnalyzeAsync(
        string model,
        string documentContent,
        CancellationToken cancellationToken);
}
