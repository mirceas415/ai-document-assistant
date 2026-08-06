using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace AI.DocumentAssistant.Server.Processing;

public sealed class PdfDocumentTextExtractor : IDocumentTextExtractor
{
    private const string PdfContentType = "application/pdf";

    public bool CanProcess(string contentType, string fileExtension) =>
        string.Equals(contentType, PdfContentType, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(fileExtension, ".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<ExtractedTextSection>> ExtractAsync(
        Stream documentStream,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var pdfDocument = PdfDocument.Open(documentStream);
            var sections = new List<ExtractedTextSection>();

            foreach (var page in pdfDocument.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var content = ContentOrderTextExtractor.GetText(page).Trim();

                if (content.Length == 0)
                {
                    continue;
                }

                sections.Add(new ExtractedTextSection(
                    sections.Count,
                    content,
                    PageNumber: page.Number));
            }

            if (sections.Count == 0)
            {
                throw new DocumentExtractionException(
                    "No extractable text was found. OCR is not supported yet.");
            }

            return Task.FromResult<IReadOnlyList<ExtractedTextSection>>(sections);
        }
        catch (DocumentExtractionException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new DocumentExtractionException(
                "The PDF could not be processed. It may be damaged or password protected.",
                exception);
        }
    }
}
