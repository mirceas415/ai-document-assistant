using AI.DocumentAssistant.Server.Ocr;
using AI.DocumentAssistant.Server.Processing;

namespace AI.DocumentAssistant.Server.Tests;

internal sealed class NoOpDocumentOcrExtractionService
    : IDocumentOcrExtractionService
{
    public Task<IReadOnlyList<ExtractedTextSection>> ApplyAsync(
        Guid documentId,
        Stream pdfStream,
        IReadOnlyList<ExtractedTextSection> nativeSections,
        bool force,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(nativeSections);
    }
}
