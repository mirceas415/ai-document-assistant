using AI.DocumentAssistant.Server.Processing;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace AI.DocumentAssistant.Server.Tests;

public sealed class DocumentTextExtractorTests
{
    [Fact]
    public async Task PdfExtractionPreservesPageOrderAndPageNumbers()
    {
        await using var stream = CreatePdf("First page text", "Second page text");
        var extractor = new PdfDocumentTextExtractor();

        var sections = await extractor.ExtractAsync(stream, CancellationToken.None);

        Assert.Collection(
            sections,
            first =>
            {
                Assert.Equal(0, first.SectionIndex);
                Assert.Equal(1, first.PageNumber);
                Assert.Contains("First page text", first.Content);
            },
            second =>
            {
                Assert.Equal(1, second.SectionIndex);
                Assert.Equal(2, second.PageNumber);
                Assert.Contains("Second page text", second.Content);
            });
    }

    [Fact]
    public async Task PdfWithoutTextFailsWithOcrMessage()
    {
        await using var stream = CreatePdf((string?)null);
        var extractor = new PdfDocumentTextExtractor();

        var exception = await Assert.ThrowsAsync<DocumentExtractionException>(
            () => extractor.ExtractAsync(stream, CancellationToken.None));

        Assert.Equal(
            "No extractable text was found. OCR is not supported yet.",
            exception.SafeMessage);
    }

    [Fact]
    public async Task DocxExtractionPreservesParagraphOrder()
    {
        await using var stream = CreateDocx(
            ParagraphWithText("First paragraph"),
            ParagraphWithText("Second paragraph"));
        var extractor = new DocxDocumentTextExtractor();

        var sections = await extractor.ExtractAsync(stream, CancellationToken.None);

        var content = Assert.Single(sections).Content;
        Assert.True(
            content.IndexOf("First paragraph", StringComparison.Ordinal) <
            content.IndexOf("Second paragraph", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DocxHeadingStylesBecomeSectionTitles()
    {
        await using var stream = CreateDocx(
            ParagraphWithText("Introduction", "Heading1"),
            ParagraphWithText("Introductory text"),
            ParagraphWithText("Details", "Heading2"),
            ParagraphWithText("Detailed text"));
        var extractor = new DocxDocumentTextExtractor();

        var sections = await extractor.ExtractAsync(stream, CancellationToken.None);

        Assert.Collection(
            sections,
            introduction =>
            {
                Assert.Equal("Introduction", introduction.SectionTitle);
                Assert.Contains("Introductory text", introduction.Content);
            },
            details =>
            {
                Assert.Equal("Details", details.SectionTitle);
                Assert.Contains("Detailed text", details.Content);
            });
    }

    [Fact]
    public async Task EmptyDocxFailsSafely()
    {
        await using var stream = CreateDocx(new Paragraph());
        var extractor = new DocxDocumentTextExtractor();

        var exception = await Assert.ThrowsAsync<DocumentExtractionException>(
            () => extractor.ExtractAsync(stream, CancellationToken.None));

        Assert.Equal(
            "No extractable text was found in the DOCX document.",
            exception.SafeMessage);
    }

    private static MemoryStream CreatePdf(params string?[] pageContents)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (var pageContent in pageContents)
        {
            var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);

            if (pageContent is not null)
            {
                page.AddText(pageContent, 12, new PdfPoint(25, 700), font);
            }
        }

        return new MemoryStream(builder.Build());
    }

    private static MemoryStream CreateDocx(params OpenXmlElement[] elements)
    {
        var stream = new MemoryStream();

        using (var document = WordprocessingDocument.Create(
                   stream,
                   WordprocessingDocumentType.Document,
                   autoSave: true))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document(new Body(elements));
            mainPart.Document.Save();
        }

        stream.Position = 0;
        return stream;
    }

    private static Paragraph ParagraphWithText(string text, string? styleId = null)
    {
        var paragraph = new Paragraph();

        if (styleId is not null)
        {
            paragraph.ParagraphProperties = new ParagraphProperties(
                new ParagraphStyleId { Val = styleId });
        }

        paragraph.AppendChild(new Run(new Text(text)));
        return paragraph;
    }
}
