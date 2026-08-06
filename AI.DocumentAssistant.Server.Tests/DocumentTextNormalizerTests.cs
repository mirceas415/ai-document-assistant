using AI.DocumentAssistant.Server.Normalization;
using AI.DocumentAssistant.Server.Chunking;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Tests;

public sealed class DocumentTextNormalizerTests
{
    [Fact]
    public void BlockDetectionDefaultsRemainBoundedAndConservative()
    {
        var options = new DocumentNormalizationOptions();

        Assert.Equal(15, options.HeaderCandidateLineCount);
        Assert.Equal(15, options.FooterCandidateLineCount);
        Assert.Equal(0.6, options.MinimumPageOccurrenceRatio);
        Assert.Equal(3, options.MinimumPageCountForBoilerplateDetection);
        Assert.Equal(40, options.MinimumCandidateBlockLength);
        Assert.Equal(160, options.MinimumLocalCandidateBlockLength);
        Assert.Equal(4_000, options.MaximumCandidateLength);
        Assert.Equal(2, options.MaximumBlockBoundaryLineOffset);
    }

    [Fact]
    public void LongRepeatedFooterBlockIsRemoved()
    {
        var footer = FictionalLongFooter();
        var pages = Enumerable.Range(1, 6)
            .Select(page => Page(page, string.Join('\n',
                BodyLines(page).Concat(footer).Append($"Page {page} / 6"))))
            .ToArray();

        var result = CreateNormalizer().Normalize(pages, isPdf: true);

        Assert.True(result.CandidateBlockCount > 0);
        Assert.True(result.ConfirmedRepeatedBlockCount > 0);
        Assert.All(result.Sections, section =>
        {
            Assert.DoesNotContain("Fictional Enterprise Holdings", section.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("All rights reserved", section.Content, StringComparison.Ordinal);
            Assert.Contains("Meaningful body paragraph", section.Content, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void LogicalBlockWithDifferentPhysicalWrappingIsRemoved()
    {
        string[][] wrappedBlocks =
        [
            [
                "Example Corporation Registered Office",
                "123 Fictional Street, Example City",
                "Registration Number ZX-12345 Confidential"
            ],
            [
                "Example Corporation",
                "Registered Office 123 Fictional Street,",
                "Example City Registration Number ZX-12345",
                "Confidential"
            ]
        ];
        var pages = Enumerable.Range(1, 4)
            .Select(page => Page(page, string.Join('\n',
                BodyLines(page)
                    .Concat(wrappedBlocks[(page - 1) % 2])
                    .Append($"{page}/4"))))
            .ToArray();

        var result = CreateNormalizer().Normalize(pages, isPdf: true);

        Assert.All(result.Sections, section =>
        {
            Assert.DoesNotContain("Example Corporation", section.Content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ZX-12345", section.Content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Meaningful body paragraph", section.Content, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void PageNumberInsideRepeatedBlockDoesNotPreventRemoval()
    {
        var pages = Enumerable.Range(1, 4)
            .Select(page => Page(page, string.Join('\n',
                BodyLines(page).Concat([
                    "Fictional Technical Manual",
                    $"Pagina {page} / 4",
                    "Revision 4.2",
                    "Controlled document — internal distribution"
                ]))))
            .ToArray();

        var result = CreateNormalizer().Normalize(pages, isPdf: true);

        Assert.All(result.Sections, section =>
        {
            Assert.DoesNotContain("Fictional Technical Manual", section.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("Pagina", section.Content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Revision 4.2", section.Content, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void HarmlessCaseSpacingAndPunctuationVariationDoesNotPreventBlockMatching()
    {
        var variants = new[]
        {
            new[] { "EXAMPLE PUBLISHER", "Registered Office , Example City", "Copyright 2026. All rights reserved" },
            new[] { "example publisher", "Registered   Office, Example City", "copyright 2026. all rights reserved" },
            new[] { "Example Publisher", "Registered Office, Example City", "Copyright 2026. All rights reserved" }
        };
        var pages = Enumerable.Range(1, 3)
            .Select(page => Page(page, string.Join('\n',
                BodyLines(page).Concat(variants[page - 1]))))
            .ToArray();

        var result = CreateNormalizer().Normalize(pages, isPdf: true);

        Assert.All(result.Sections, section =>
            Assert.DoesNotContain("publisher", section.Content, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MateriallyDifferentBlocksAreNotMerged()
    {
        var firstBlock = new[]
        {
            "Alpha Reference AA-100",
            "Policy edition one is active"
        };
        var secondBlock = new[]
        {
            "Beta Manual BB-900",
            "Revision content is materially different"
        };
        var pages = Enumerable.Range(1, 4)
            .Select(page => Page(page, string.Join('\n',
                BodyLines(page).Concat(page <= 2 ? firstBlock : secondBlock))))
            .ToArray();
        var normalizer = CreateNormalizer(new DocumentNormalizationOptions
        {
            HeaderCandidateLineCount = 15,
            FooterCandidateLineCount = 15,
            MinimumPageOccurrenceRatio = 0.75,
            MinimumPageCountForBoilerplateDetection = 3,
            MinimumCandidateBlockLength = 40,
            MinimumLocalCandidateBlockLength = 160,
            MaximumCandidateLength = 4_000,
            MaximumBlockBoundaryLineOffset = 2
        });

        var result = normalizer.Normalize(pages, isPdf: true);

        Assert.Contains("Alpha Reference AA-100", result.Sections[0].Content, StringComparison.Ordinal);
        Assert.Contains("Beta Manual BB-900", result.Sections[2].Content, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedHeaderCopiesAreRemovedWhileBodyOccurrenceRemains()
    {
        var pages = Enumerable.Range(1, 4)
            .Select(page => Page(page, string.Join('\n', new[]
            {
                "Fictional University",
                "Distributed Systems — Lecture Notes"
            }.Concat(BodyLines(page, page == 1 ? "Fictional University" : null)))))
            .ToArray();

        var result = CreateNormalizer().Normalize(pages, isPdf: true);

        Assert.Equal(1, CountOccurrences(result.Sections[0].Content, "Fictional University"));
        Assert.All(result.Sections.Skip(1), section =>
            Assert.DoesNotContain("Fictional University", section.Content, StringComparison.Ordinal));
    }

    [Fact]
    public void DenseLocalLongBlockIsDetectedWithoutLoweringGlobalRatio()
    {
        var pages = Enumerable.Range(1, 8)
            .Select(page => Page(page, string.Join('\n',
                BodyLines(page).Concat(page is >= 3 and <= 5
                    ? FictionalLongFooter()
                    : [$"Unique page ending {page}"]))))
            .ToArray();

        var result = CreateNormalizer().Normalize(pages, isPdf: true);

        Assert.Equal(0.6, new DocumentNormalizationOptions().MinimumPageOccurrenceRatio);
        Assert.DoesNotContain("Fictional Enterprise Holdings", result.Sections[2].Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Fictional Enterprise Holdings", result.Sections[3].Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Fictional Enterprise Holdings", result.Sections[4].Content, StringComparison.Ordinal);
        Assert.Contains("Unique page ending 1", result.Sections[0].Content, StringComparison.Ordinal);
    }

    [Fact]
    public void SyntheticEnterpriseStressFixturePreservesRawBodyAndCleansChunks()
    {
        var footer = FictionalLongFooter();
        var rawSections = Enumerable.Range(1, 6)
            .Select(page => Page(page, string.Join('\n',
                BodyLines(page, "shared system terminology")
                    .Concat(page % 2 == 0
                        ? RewrapFooter(footer)
                        : footer)
                    .Append($"Page {page} / 6"))))
            .ToArray();
        var normalizer = CreateNormalizer();

        var first = normalizer.Normalize(rawSections, isPdf: true);
        var second = normalizer.Normalize(rawSections, isPdf: true);
        var idempotent = normalizer.Normalize(first.Sections.Select(section =>
            new NormalizationSourceSection(
                section.SectionIndex,
                section.Content,
                section.PageNumber,
                section.SectionTitle)).ToArray(), isPdf: true);
        var chunkGenerator = new DocumentChunkGenerator(
            new Cl100kDocumentTokenizer(),
            Options.Create(new DocumentChunkingOptions()));
        var chunks = chunkGenerator.Generate(first.Sections.Select(section =>
            new ChunkSourceSection(
                section.SectionIndex,
                section.Content,
                section.PageNumber,
                section.SectionTitle)).ToArray());

        Assert.All(rawSections, section =>
            Assert.Contains("Fictional Enterprise Holdings", section.Content, StringComparison.Ordinal));
        Assert.All(first.Sections, section =>
        {
            Assert.DoesNotContain("Fictional Enterprise Holdings", section.Content, StringComparison.Ordinal);
            Assert.Contains("shared system terminology", section.Content, StringComparison.Ordinal);
            Assert.Contains($"{section.PageNumber}. SYSTEM ARCHITECTURE", section.Content, StringComparison.Ordinal);
        });
        Assert.All(chunks, chunk =>
            Assert.DoesNotContain("Fictional Enterprise Holdings", chunk.Content, StringComparison.Ordinal));
        Assert.Equal(
            first.Sections.Select(section => section.Content),
            second.Sections.Select(section => section.Content));
        Assert.Equal(
            first.Sections.Select(section => section.Content),
            idempotent.Sections.Select(section => section.Content));
    }

    [Fact]
    public void RepeatedExactHeaderLinesAreRemoved()
    {
        var result = NormalizePdf([
            Page(1, "CONFIDENTIAL\nFirst body"),
            Page(2, "CONFIDENTIAL\nSecond body"),
            Page(3, "CONFIDENTIAL\nThird body")
        ], headerLines: 1, footerLines: 1);

        Assert.All(result.Sections, section =>
            Assert.DoesNotContain("CONFIDENTIAL", section.Content, StringComparison.Ordinal));
    }

    [Fact]
    public void RepeatedExactFooterLinesAreRemoved()
    {
        var result = NormalizePdf([
            Page(1, "First body\nLegal footer"),
            Page(2, "Second body\nLegal footer"),
            Page(3, "Third body\nLegal footer")
        ], headerLines: 1, footerLines: 1);

        Assert.All(result.Sections, section =>
            Assert.DoesNotContain("Legal footer", section.Content, StringComparison.Ordinal));
    }

    [Fact]
    public void MultiLineRepeatedBoilerplateIsRemoved()
    {
        var pages = Enumerable.Range(1, 3)
            .Select(page => Page(page, $"Form code 11.13\nCompany address\nMeaningful body {page}"))
            .ToArray();

        var result = NormalizePdf(pages, headerLines: 2, footerLines: 1);

        Assert.All(result.Sections, section =>
        {
            Assert.DoesNotContain("Form code", section.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("Company address", section.Content, StringComparison.Ordinal);
            Assert.Contains("Meaningful body", section.Content, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void BoilerplateBelowOccurrenceThresholdIsPreserved()
    {
        var result = NormalizePdf([
            Page(1, "Occasional header\nBody one"),
            Page(2, "Occasional header\nBody two"),
            Page(3, "Different header\nBody three"),
            Page(4, "Another header\nBody four")
        ], headerLines: 1, footerLines: 1, ratio: 0.75);

        Assert.Contains("Occasional header", result.Sections[0].Content, StringComparison.Ordinal);
        Assert.Contains("Occasional header", result.Sections[1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedBodyTextOutsideCandidateRegionsIsPreserved()
    {
        var result = NormalizePdf([
            Page(1, "Header one\nRepeated legal body\nFooter one"),
            Page(2, "Header two\nRepeated legal body\nFooter two"),
            Page(3, "Header three\nRepeated legal body\nFooter three")
        ], headerLines: 1, footerLines: 1);

        Assert.All(result.Sections, section =>
            Assert.Contains("Repeated legal body", section.Content, StringComparison.Ordinal));
    }

    [Fact]
    public void TwoPageDocumentDoesNotApplyRepetitionRemoval()
    {
        var result = NormalizePdf([
            Page(1, "Repeated header\nBody one"),
            Page(2, "Repeated header\nBody two")
        ], headerLines: 1, footerLines: 1);

        Assert.All(result.Sections, section =>
            Assert.Contains("Repeated header", section.Content, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("Page 1")]
    [InlineData("Pagina 1")]
    [InlineData("1 / 14")]
    [InlineData("1/14")]
    public void StandalonePageNumbersInCandidateRegionsAreRemoved(string pageLine)
    {
        var result = NormalizePdf([
            Page(1, $"Body content\n{pageLine}"),
            Page(2, "Other body\n2"),
            Page(3, "Third body\n3")
        ], headerLines: 1, footerLines: 1);

        Assert.Equal("Body content", result.Sections[0].Content);
    }

    [Fact]
    public void BodyNumbersAndMismatchedStandaloneNumbersArePreserved()
    {
        var result = NormalizePdf([
            Page(1, "Page 1\nAmount 7000\nContract 42\n99"),
            Page(2, "Page 2\nAmount 8000\nContract 43\n98"),
            Page(3, "Page 3\nAmount 9000\nContract 44\n97")
        ], headerLines: 1, footerLines: 1);

        Assert.Contains("Amount 7000", result.Sections[0].Content, StringComparison.Ordinal);
        Assert.Contains("Contract 42", result.Sections[0].Content, StringComparison.Ordinal);
        Assert.EndsWith("99", result.Sections[0].Content, StringComparison.Ordinal);
    }

    [Fact]
    public void NumberedSectionHeadingsAreProtectedFromRepetitionRemoval()
    {
        var result = NormalizePdf([
            Page(1, "1. DATE IDENTIFICARE\nBody one"),
            Page(2, "1. DATE IDENTIFICARE\nBody two"),
            Page(3, "1. DATE IDENTIFICARE\nBody three")
        ], headerLines: 1, footerLines: 1);

        Assert.All(result.Sections, section =>
            Assert.StartsWith("1. DATE IDENTIFICARE", section.Content, StringComparison.Ordinal));
    }

    [Fact]
    public void WhitespaceAndBlankLinesAreNormalizedWithoutLosingParagraphs()
    {
        var result = Normalize([
            new(0, "  Primul   paragraf\tcu spații.  \r\n\r\n \r\nAl doilea paragraf.  ")
        ]);

        Assert.Equal(
            "Primul paragraf cu spații.\n\nAl doilea paragraf.",
            result.Sections[0].Content);
    }

    [Fact]
    public void SafeLineBreakHyphenationIsRepaired()
    {
        var result = Normalize([new(0, "Contractul furnizo-\nrului este activ.")]);

        Assert.Equal("Contractul furnizorului este activ.", result.Sections[0].Content);
    }

    [Fact]
    public void MeaningfulHyphenatedWordsAndNumericIdentifiersArePreserved()
    {
        var result = Normalize([new(0, "A well-known term.\nCod AB-\n1234")]);

        Assert.Contains("well-known", result.Sections[0].Content, StringComparison.Ordinal);
        Assert.Contains("AB-\n1234", result.Sections[0].Content, StringComparison.Ordinal);
    }

    [Fact]
    public void RomanianEnglishMixedUnicodeAndSurrogatePairsArePreserved()
    {
        const string content = "Română: ă â î ș ț. English text. Mixed contract și agreement. Emoji 😀.";

        var result = Normalize([new(0, content)]);

        Assert.Equal(content, result.Sections[0].Content);
    }

    [Fact]
    public void NormalizationIsDeterministicAndIdempotent()
    {
        var source = new[]
        {
            Page(1, "HEADER\nText   furnizo-\nrului\nPage 1"),
            Page(2, "HEADER\nAlt text\nPage 2"),
            Page(3, "HEADER\nThird text\nPage 3")
        };

        var first = NormalizePdf(source, headerLines: 1, footerLines: 1);
        var repeated = NormalizePdf(source, headerLines: 1, footerLines: 1);
        var second = NormalizePdf(
            first.Sections.Select(section => new NormalizationSourceSection(
                section.SectionIndex,
                section.Content,
                section.PageNumber,
                section.SectionTitle)).ToArray(),
            headerLines: 1,
            footerLines: 1);

        Assert.Equal(
            first.Sections.Select(section => section.Content),
            repeated.Sections.Select(section => section.Content));
        Assert.Equal(
            first.Sections.Select(section => section.Content),
            second.Sections.Select(section => section.Content));
    }

    [Fact]
    public void AllBoilerplatePageMayBecomeEmptyWhenMeaningfulTextExistsElsewhere()
    {
        var result = NormalizePdf([
            Page(1, "First meaningful body\nRepeated footer"),
            Page(2, "Second meaningful body\nRepeated footer"),
            Page(3, "Third meaningful body\nRepeated footer"),
            Page(4, "Repeated footer")
        ], headerLines: 1, footerLines: 1);

        Assert.Equal("First meaningful body", result.Sections[0].Content);
        Assert.Equal("Second meaningful body", result.Sections[1].Content);
        Assert.Equal("Third meaningful body", result.Sections[2].Content);
        Assert.Equal(string.Empty, result.Sections[3].Content);
    }

    [Fact]
    public void WholeDocumentSafetyPreservesContentWhenEveryLineLooksRepeated()
    {
        var result = NormalizePdf([
            Page(1, "Same meaningful clause"),
            Page(2, "Same meaningful clause"),
            Page(3, "Same meaningful clause")
        ], headerLines: 1, footerLines: 1);

        Assert.All(result.Sections, section => Assert.Equal("Same meaningful clause", section.Content));
    }

    [Fact]
    public void RepresentativeBodyFixtureLosesOnlyConfirmedBoilerplate()
    {
        var pages = Enumerable.Range(1, 5)
            .Select(page => Page(page,
                $"Raiffeisen Bank S.A. • Administrația Centrală\n" +
                $"Telefon 021 000 0000\n" +
                $"{page}. DATE REZIDENȚĂ FISCALĂ\n" +
                $"Conținut semnificativ unic pentru pagina {page}.\n" +
                $"Pagina {page}"))
            .ToArray();

        var result = NormalizePdf(pages, headerLines: 2, footerLines: 1);

        Assert.All(result.Sections, section =>
        {
            Assert.DoesNotContain("Administrația Centrală", section.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("Telefon", section.Content, StringComparison.Ordinal);
            Assert.Contains("DATE REZIDENȚĂ FISCALĂ", section.Content, StringComparison.Ordinal);
            Assert.Contains("Conținut semnificativ", section.Content, StringComparison.Ordinal);
        });
    }

    private static NormalizationSourceSection Page(int pageNumber, string content) =>
        new(pageNumber - 1, content, pageNumber);

    private static IEnumerable<string> BodyLines(int pageNumber, string? extraMiddleLine = null)
    {
        yield return $"{pageNumber}. SYSTEM ARCHITECTURE";
        for (var index = 1; index <= 20; index++)
        {
            yield return index == 10 && extraMiddleLine is not null
                ? extraMiddleLine
                : $"Meaningful body paragraph {pageNumber}-{index} with Romanian text ă â î ș ț and English content 😀.";
        }
    }

    private static string[] FictionalLongFooter() =>
    [
        "Fictional Enterprise Holdings S.A.",
        "Registered office: 100 Example Avenue",
        "Example City, Fictionland",
        "Registration number FEH-2026-001",
        "Technical document reference GEN-42",
        "Revision 5.3",
        "Controlled distribution copy",
        "Contact centre: +40 000 000 000",
        "General correspondence department",
        "Confidential enterprise information",
        "Copyright 2026 Fictional Enterprise Holdings",
        "All rights reserved"
    ];

    private static IEnumerable<string> RewrapFooter(IReadOnlyList<string> footer)
    {
        yield return $"{footer[0]} {footer[1]}";
        yield return footer[2];
        yield return $"{footer[3]} {footer[4]}";
        yield return footer[5];
        yield return $"{footer[6]} {footer[7]}";
        yield return footer[8];
        yield return $"{footer[9]} {footer[10]}";
        yield return footer[11];
    }

    private static int CountOccurrences(string content, string value) =>
        content.Split(value, StringSplitOptions.None).Length - 1;

    private static DocumentNormalizationResult Normalize(
        IReadOnlyList<NormalizationSourceSection> sections) =>
        CreateNormalizer().Normalize(sections, isPdf: false);

    private static DocumentNormalizationResult NormalizePdf(
        IReadOnlyList<NormalizationSourceSection> sections,
        int headerLines,
        int footerLines,
        double ratio = 0.6) =>
        CreateNormalizer(new DocumentNormalizationOptions
        {
            HeaderCandidateLineCount = headerLines,
            FooterCandidateLineCount = footerLines,
            MinimumPageOccurrenceRatio = ratio,
            MinimumPageCountForBoilerplateDetection = 3,
            MinimumCandidateBlockLength = 40,
            MinimumLocalCandidateBlockLength = 160,
            MaximumCandidateLength = 4_000,
            MaximumBlockBoundaryLineOffset = 2,
            EnablePageNumberRemoval = true,
            EnableWordBreakRepair = true
        }).Normalize(sections, isPdf: true);

    private static DocumentTextNormalizer CreateNormalizer(
        DocumentNormalizationOptions? options = null) =>
        new(Options.Create(options ?? new DocumentNormalizationOptions()));
}
