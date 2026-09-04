using AI.DocumentAssistant.Server.Processing;

namespace AI.DocumentAssistant.Server.Ocr;

public interface IDocumentOcrExtractionService
{
    Task<IReadOnlyList<ExtractedTextSection>> ApplyAsync(
        Guid documentId,
        Stream pdfStream,
        IReadOnlyList<ExtractedTextSection> nativeSections,
        bool force,
        CancellationToken cancellationToken);
}
