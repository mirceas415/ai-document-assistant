using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using AI.DocumentAssistant.Server.Models;

namespace AI.DocumentAssistant.Server.Processing;

public sealed class DocxDocumentTextExtractor : IDocumentTextExtractor
{
    private const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public bool CanProcess(string contentType, string fileExtension) =>
        string.Equals(contentType, DocxContentType, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(fileExtension, ".docx", StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<ExtractedTextSection>> ExtractAsync(
        Stream documentStream,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var wordDocument = WordprocessingDocument.Open(
                documentStream,
                false,
                new OpenSettings { AutoSave = false });

            var body = wordDocument.MainDocumentPart?.Document?.Body
                ?? throw new DocumentExtractionException(
                    "The DOCX document does not contain a readable document body.");

            var sections = new List<ExtractedTextSection>();
            var currentLines = new List<string>();
            string? currentTitle = null;

            foreach (var element in body.ChildElements)
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (element)
                {
                    case Paragraph paragraph:
                    {
                        var paragraphText = GetParagraphText(paragraph);

                        if (paragraphText.Length == 0)
                        {
                            continue;
                        }

                        if (IsHeading(paragraph))
                        {
                            FlushSection(sections, currentLines, currentTitle);
                            currentTitle = Truncate(paragraphText, 500);
                        }

                        currentLines.Add(paragraphText);
                        break;
                    }
                    case Table table:
                    {
                        var tableText = GetTableText(table);

                        if (tableText.Length > 0)
                        {
                            currentLines.Add(tableText);
                        }

                        break;
                    }
                }
            }

            FlushSection(sections, currentLines, currentTitle);

            if (sections.Count == 0)
            {
                throw new DocumentExtractionException(
                    "No extractable text was found in the DOCX document.");
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
                "The DOCX document could not be processed. It may be damaged or unsupported.",
                exception);
        }
    }

    private static void FlushSection(
        ICollection<ExtractedTextSection> sections,
        ICollection<string> currentLines,
        string? currentTitle)
    {
        var content = string.Join(Environment.NewLine, currentLines).Trim();

        if (content.Length > 0)
        {
            sections.Add(new ExtractedTextSection(
                sections.Count,
                content,
                SectionTitle: currentTitle,
                ExtractionMethod: DocumentTextExtractionMethod.Docx));
        }

        currentLines.Clear();
    }

    private static string GetParagraphText(Paragraph paragraph) =>
        string.Concat(paragraph.Descendants<Text>().Select(text => text.Text)).Trim();

    private static string GetTableText(Table table)
    {
        var rows = table.Elements<TableRow>()
            .Select(row => row.Elements<TableCell>()
                .Select(cell => string.Join(
                    " ",
                    cell.Elements<Paragraph>()
                        .Select(GetParagraphText)
                        .Where(text => text.Length > 0)))
                .Where(text => text.Length > 0)
                .ToArray())
            .Where(cells => cells.Length > 0)
            .Select(cells => string.Join(" | ", cells));

        return string.Join(Environment.NewLine, rows).Trim();
    }

    private static bool IsHeading(Paragraph paragraph)
    {
        var styleId = paragraph.ParagraphProperties?
            .ParagraphStyleId?
            .Val?
            .Value;

        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        var normalizedStyleId = styleId
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        return normalizedStyleId.Equals("Heading1", StringComparison.OrdinalIgnoreCase) ||
               normalizedStyleId.Equals("Heading2", StringComparison.OrdinalIgnoreCase) ||
               normalizedStyleId.Equals("Heading3", StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
