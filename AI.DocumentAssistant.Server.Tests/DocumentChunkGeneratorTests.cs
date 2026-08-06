using System.Text.RegularExpressions;
using AI.DocumentAssistant.Server.Chunking;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Tests;

public sealed partial class DocumentChunkGeneratorTests
{
    [Fact]
    public void ShortDocumentProducesSingleChunk()
    {
        var generator = CreateGenerator();
        const string text = "Acesta este un document scurt, păstrat integral.";

        var chunk = Assert.Single(generator.Generate([
            new ChunkSourceSection(0, text, PageNumber: 1, SectionTitle: "Introducere")
        ]));

        Assert.Equal(text, chunk.Content);
        Assert.Equal(text.Length, chunk.CharacterCount);
        Assert.True(chunk.TokenCount > 0);
        Assert.Equal(1, chunk.PageStart);
        Assert.Equal(1, chunk.PageEnd);
        Assert.Equal("Introducere", chunk.SectionTitle);
        Assert.Equal(0, chunk.SourceSectionStartIndex);
        Assert.Equal(0, chunk.SourceSectionEndIndex);
    }

    [Fact]
    public void LongDocumentRespectsLimitsOrderingAndPreservesContent()
    {
        var generator = CreateGenerator(target: 80, maximum: 100, overlap: 15, minimum: 20);
        var sentences = Enumerable.Range(0, 80)
            .Select(index => $"Sentence marker-{index:D3} contains deterministic English retrieval text.")
            .ToArray();
        var text = string.Join(' ', sentences);

        var chunks = generator.Generate([new ChunkSourceSection(0, text)]);

        Assert.True(chunks.Count > 1);
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(chunk => chunk.ChunkIndex));
        Assert.All(chunks, chunk =>
        {
            Assert.InRange(chunk.TokenCount, 1, 100);
            Assert.Equal(chunk.Content.Length, chunk.CharacterCount);
        });
        Assert.All(sentences, sentence =>
            Assert.Contains(chunks, chunk => chunk.Content.Contains(sentence, StringComparison.Ordinal)));
    }

    [Fact]
    public void ConsecutiveChunksRepeatUsefulOverlapWithoutDuplicatingWholeChunk()
    {
        var generator = CreateGenerator(target: 55, maximum: 75, overlap: 15, minimum: 15);
        var text = string.Join(' ', Enumerable.Range(0, 50)
            .Select(index => $"Context sentence {index:D3} remains intact for retrieval."));

        var chunks = generator.Generate([new ChunkSourceSection(0, text)]);

        Assert.True(chunks.Count > 2);

        for (var index = 1; index < chunks.Count; index++)
        {
            var previousMarkers = MarkerRegex().Matches(chunks[index - 1].Content)
                .Select(match => match.Value)
                .ToHashSet(StringComparer.Ordinal);
            var currentMarkers = MarkerRegex().Matches(chunks[index].Content)
                .Select(match => match.Value)
                .ToHashSet(StringComparer.Ordinal);

            Assert.NotEmpty(previousMarkers.Intersect(currentMarkers));
            Assert.NotEqual(chunks[index - 1].Content, chunks[index].Content);
        }
    }

    [Fact]
    public void SentenceBoundariesArePreservedWhenTheyFitMaximum()
    {
        var generator = CreateGenerator(target: 15, maximum: 30, overlap: 0, minimum: 5);
        var sentences = new[]
        {
            "The first complete sentence stays together.",
            "The second complete sentence also stays together.",
            "The third complete sentence closes the example."
        };

        var chunks = generator.Generate([
            new ChunkSourceSection(0, string.Join(' ', sentences))
        ]);

        Assert.All(sentences, sentence =>
            Assert.Contains(chunks, chunk => chunk.Content.Contains(sentence, StringComparison.Ordinal)));
    }

    [Fact]
    public void RomanianAbbreviationsDoNotCreateSentenceBreaks()
    {
        var generator = CreateGenerator(target: 25, maximum: 45, overlap: 0, minimum: 5);
        const string firstSentence =
            "Conform art. 5 și nr. 12, dl. Popescu prezintă informația corectă.";
        var text = $"{firstSentence} A doua propoziție explică rezultatul în limba română.";

        var chunks = generator.Generate([new ChunkSourceSection(0, text)]);

        Assert.Contains(chunks, chunk =>
            chunk.Content.Contains(firstSentence, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Română: ă â î ș ț — informații despre învățământ.")]
    [InlineData("English: A clear document retrieval sentence.")]
    [InlineData("Mixt: informații în română + an English explanation.")]
    [InlineData("Unicode: Știință, București, €uro, emoji 😊 și café.")]
    public void RomanianEnglishMixedAndUnicodeTextIsPreserved(string text)
    {
        var chunk = Assert.Single(CreateGenerator().Generate([
            new ChunkSourceSection(0, text)
        ]));

        Assert.Equal(text, chunk.Content);
        Assert.Equal(text.Length, chunk.CharacterCount);
    }

    [Fact]
    public void HeadingsPagesAndSourceSectionIndexesArePreserved()
    {
        var generator = CreateGenerator();
        var chunks = generator.Generate([
            new ChunkSourceSection(0, "Introducere\nConținut pe prima pagină.", 1, "Introducere"),
            new ChunkSourceSection(1, "Content on the second page.", 2)
        ]);

        var chunk = Assert.Single(chunks);
        Assert.Contains("Introducere", chunk.Content);
        Assert.Equal("Introducere", chunk.SectionTitle);
        Assert.Equal(1, chunk.PageStart);
        Assert.Equal(2, chunk.PageEnd);
        Assert.Equal(0, chunk.SourceSectionStartIndex);
        Assert.Equal(1, chunk.SourceSectionEndIndex);
    }

    [Fact]
    public void GenerationIsDeterministic()
    {
        var generator = CreateGenerator(target: 50, maximum: 70, overlap: 10, minimum: 10);
        var sections = new[]
        {
            new ChunkSourceSection(0, string.Join(' ', Enumerable.Repeat(
                "Textul determinist rămâne identic. Deterministic text remains identical.", 20)), 1),
            new ChunkSourceSection(1, "Concluzie cu diacritice: ă â î ș ț.", 2, "Concluzie")
        };

        var first = generator.Generate(sections);
        var second = generator.Generate(sections);

        Assert.Equal(first, second);
    }

    private static DocumentChunkGenerator CreateGenerator(
        int target = 700,
        int maximum = 900,
        int overlap = 100,
        int minimum = 100) =>
        new(
            new Cl100kDocumentTokenizer(),
            Options.Create(new DocumentChunkingOptions
            {
                TargetTokens = target,
                MaximumTokens = maximum,
                OverlapTokens = overlap,
                MinimumTokens = minimum
            }));

    [GeneratedRegex(@"\b\d{3}\b", RegexOptions.CultureInvariant)]
    private static partial Regex MarkerRegex();
}
