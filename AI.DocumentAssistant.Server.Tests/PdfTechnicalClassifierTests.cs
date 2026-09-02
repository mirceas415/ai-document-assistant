using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.TechnicalAnalysis;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace AI.DocumentAssistant.Server.Tests;

public sealed class PdfTechnicalClassifierTests
{
    [Fact]
    public void MeaningfulTextRequiresEnoughAlphanumericCharactersOrUsefulWords()
    {
        Assert.False(PdfTechnicalClassifier.HasMeaningfulText(39, 7));
        Assert.True(PdfTechnicalClassifier.HasMeaningfulText(40, 1));
        Assert.True(PdfTechnicalClassifier.HasMeaningfulText(16, 8));

        var measured = PdfTechnicalClassifier.MeasureText(
            "Page 7 footer watermark");
        Assert.Equal(20, measured.AlphanumericCharacterCount);
        Assert.Equal(3, measured.UsefulWordCount);
        Assert.False(PdfTechnicalClassifier.HasMeaningfulText(
            measured.AlphanumericCharacterCount,
            measured.UsefulWordCount));
    }

    [Fact]
    public void MeaningfulTextWithLittleImageCoverageIsTextBased()
    {
        var result = Classify(textCharacters: 120, words: 15, images: 1, coverage: 0.12);

        Assert.Equal(TechnicalType.TextBased, result.TechnicalType);
        Assert.True(result.HasMeaningfulText);
        Assert.False(result.HasPageSizedImage);
    }

    [Fact]
    public void NoMeaningfulTextAndPageSizedImageIsScanned()
    {
        var result = Classify(textCharacters: 3, words: 1, images: 1, coverage: 0.94);

        Assert.Equal(TechnicalType.Scanned, result.TechnicalType);
        Assert.False(result.HasMeaningfulText);
        Assert.True(result.HasPageSizedImage);
    }

    [Fact]
    public void NoMeaningfulTextAndNonPageSizedImageIsImageBased()
    {
        var result = Classify(textCharacters: 0, words: 0, images: 2, coverage: 0.42);

        Assert.Equal(TechnicalType.ImageBased, result.TechnicalType);
        Assert.False(result.HasPageSizedImage);
    }

    [Fact]
    public void MeaningfulTextAndSubstantialImageCoverageIsMixed()
    {
        var result = Classify(textCharacters: 80, words: 10, images: 1, coverage: 0.30);

        Assert.Equal(TechnicalType.Mixed, result.TechnicalType);
    }

    [Fact]
    public void BlankPageIsUnknown()
    {
        var result = Classify(textCharacters: 0, words: 0, images: 0, coverage: 0);

        Assert.Equal(TechnicalType.Unknown, result.TechnicalType);
        Assert.False(result.HasMeaningfulText);
        Assert.False(result.HasPageSizedImage);
    }

    [Fact]
    public void PageSizedImageThresholdIsInclusiveAndConservative()
    {
        var below = Classify(0, 0, 1, 0.7999);
        var atThreshold = Classify(0, 0, 1, 0.80);

        Assert.Equal(TechnicalType.ImageBased, below.TechnicalType);
        Assert.False(below.HasPageSizedImage);
        Assert.Equal(TechnicalType.Scanned, atThreshold.TechnicalType);
        Assert.True(atThreshold.HasPageSizedImage);
    }

    [Theory]
    [InlineData(-0.2, 0.0)]
    [InlineData(0.25, 0.25)]
    [InlineData(1.4, 1.0)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(double.PositiveInfinity, 0.0)]
    public void ImageCoverageIsAlwaysClamped(double input, double expected)
    {
        Assert.Equal(expected, PdfTechnicalClassifier.ClampCoverage(input));
    }

    [Fact]
    public void PageSizedImageWithExistingMeaningfulOcrTextLayerIsMixed()
    {
        var result = Classify(textCharacters: 500, words: 80, images: 1, coverage: 0.97);

        Assert.Equal(TechnicalType.Mixed, result.TechnicalType);
        Assert.True(result.HasMeaningfulText);
        Assert.True(result.HasPageSizedImage);
    }

    [Fact]
    public void StrongDocumentMajorityWinsWithoutOneDecorativePageForcingMixed()
    {
        var pages = Enumerable.Range(1, 99)
            .Select(number => Page(number, TechnicalType.TextBased))
            .Append(Page(100, TechnicalType.ImageBased))
            .ToArray();

        Assert.Equal(
            TechnicalType.TextBased,
            PdfTechnicalClassifier.ClassifyDocument(pages));
    }

    [Fact]
    public void MateriallyDifferentPageTypesProduceMixedDocument()
    {
        var pages = Enumerable.Range(1, 7)
            .Select(number => Page(number, TechnicalType.TextBased))
            .Concat(Enumerable.Range(8, 3)
                .Select(number => Page(number, TechnicalType.Scanned)))
            .ToArray();

        Assert.Equal(
            TechnicalType.Mixed,
            PdfTechnicalClassifier.ClassifyDocument(pages));
    }

    [Fact]
    public void BlankPagesWithinToleranceDoNotForceMixed()
    {
        var pages = Enumerable.Range(1, 8)
            .Select(number => Page(number, TechnicalType.Scanned))
            .Concat([
                Page(9, TechnicalType.Unknown),
                Page(10, TechnicalType.Unknown)
            ])
            .ToArray();

        Assert.Equal(
            TechnicalType.Scanned,
            PdfTechnicalClassifier.ClassifyDocument(pages));
    }

    [Fact]
    public void TooManyUnknownPagesLeaveDocumentUnknown()
    {
        var pages = new[]
        {
            Page(1, TechnicalType.TextBased),
            Page(2, TechnicalType.TextBased),
            Page(3, TechnicalType.Unknown),
            Page(4, TechnicalType.Unknown)
        };

        Assert.Equal(
            TechnicalType.Unknown,
            PdfTechnicalClassifier.ClassifyDocument(pages));
    }

    [Fact]
    public void EightyPercentDocumentMajorityIsInclusiveAndResultsAreDeterministic()
    {
        var pages = Enumerable.Range(1, 8)
            .Select(number => Page(number, TechnicalType.ImageBased))
            .Concat(Enumerable.Range(9, 2)
                .Select(number => Page(number, TechnicalType.TextBased)))
            .ToArray();

        var results = Enumerable.Range(0, 20)
            .Select(_ => PdfTechnicalClassifier.ClassifyDocument(pages))
            .Distinct()
            .ToArray();

        Assert.Equal([TechnicalType.ImageBased], results);
    }

    [Fact]
    public void AnalyzerVersionIdentifiesVersionedHeuristics()
    {
        IPdfTechnicalAnalyzer analyzer = new PdfPigPdfTechnicalAnalyzer();

        Assert.Equal("pdf-technical-analysis-v1", analyzer.AnalyzerVersion);
    }

    [Fact]
    public async Task PdfPigAnalyzerReadsOriginalPdfPageStructureDeterministically()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var textPage = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        textPage.AddText(
            "This page has a meaningful native PDF text layer with enough useful words for deterministic classification.",
            12,
            new PdfPoint(25, 700),
            font);
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        using var stream = new MemoryStream(builder.Build());
        var analyzer = new PdfPigPdfTechnicalAnalyzer();

        var first = await analyzer.AnalyzeAsync(stream, CancellationToken.None);
        stream.Position = 0;
        var second = await analyzer.AnalyzeAsync(stream, CancellationToken.None);

        Assert.Equal(first.TechnicalType, second.TechnicalType);
        Assert.Equal(first.Pages.ToArray(), second.Pages.ToArray());
        Assert.Equal(TechnicalType.TextBased, first.TechnicalType);
        Assert.Collection(
            first.Pages,
            page =>
            {
                Assert.Equal(TechnicalType.TextBased, page.TechnicalType);
                Assert.True(page.HasMeaningfulText);
                Assert.Equal(0, page.ImageCount);
            },
            page => Assert.Equal(TechnicalType.Unknown, page.TechnicalType));
    }

    private static PdfPageTechnicalAnalysisResult Classify(
        int textCharacters,
        int words,
        int images,
        double coverage) =>
        PdfTechnicalClassifier.ClassifyPage(new PdfPageTechnicalMetrics(
            1,
            textCharacters,
            words,
            images,
            coverage));

    private static PdfPageTechnicalAnalysisResult Page(
        int number,
        TechnicalType type) =>
        new(
            number,
            type,
            0,
            0,
            0,
            0,
            false,
            false);
}
