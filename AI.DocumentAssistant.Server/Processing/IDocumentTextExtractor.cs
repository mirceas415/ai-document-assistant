namespace AI.DocumentAssistant.Server.Processing;

public interface IDocumentTextExtractor
{
    bool CanProcess(string contentType, string fileExtension);

    Task<IReadOnlyList<ExtractedTextSection>> ExtractAsync(
        Stream documentStream,
        CancellationToken cancellationToken);
}
