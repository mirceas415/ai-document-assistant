using System.Text;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Chunking;

public sealed class DocumentChunkGenerator : IDocumentChunkGenerator
{
    private static readonly HashSet<string> ProtectedAbbreviations = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "art", "nr", "dl", "dna", "d-na", "dr", "prof", "etc",
        "mr", "mrs", "ms", "no", "vs", "e.g", "i.e"
    };

    private readonly IDocumentTokenizer _tokenizer;
    private readonly DocumentChunkingOptions _options;

    public DocumentChunkGenerator(
        IDocumentTokenizer tokenizer,
        IOptions<DocumentChunkingOptions> options)
    {
        _tokenizer = tokenizer;
        _options = options.Value;
    }

    public IReadOnlyList<GeneratedDocumentChunk> Generate(
        IReadOnlyList<ChunkSourceSection> sourceSections,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceSections);

        var units = CreateUnits(sourceSections, cancellationToken);
        if (units.Count == 0)
        {
            throw new DocumentChunkingException(
                "No extracted text is available for chunking.");
        }

        var basePlans = CreateBasePlans(units, cancellationToken);
        RebalanceFinalPlan(basePlans);

        var results = new List<GeneratedDocumentChunk>(basePlans.Count);

        for (var index = 0; index < basePlans.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var basePlan = basePlans[index];
            var combinedUnits = new List<TextUnit>();

            if (index > 0 && _options.OverlapTokens > 0)
            {
                var baseTokenCount = CountTokens(basePlan.Units);
                var overlapBudget = Math.Min(
                    _options.OverlapTokens,
                    Math.Max(0, _options.MaximumTokens - baseTokenCount));

                combinedUnits.AddRange(CreateOverlapUnits(
                    basePlans[index - 1],
                    overlapBudget));
            }

            combinedUnits.AddRange(basePlan.Units);
            TrimOverlapToMaximum(combinedUnits, basePlan.Units.Count);

            var content = BuildContent(combinedUnits);
            var tokenCount = _tokenizer.CountTokens(content);

            if (content.Length == 0 || tokenCount == 0)
            {
                throw new DocumentChunkingException(
                    "Chunk generation produced an empty chunk.");
            }

            if (tokenCount > _options.MaximumTokens)
            {
                throw new DocumentChunkingException(
                    "Chunk generation exceeded the configured token limit.");
            }

            var pages = combinedUnits
                .Where(unit => unit.PageNumber.HasValue)
                .Select(unit => unit.PageNumber!.Value)
                .ToArray();
            var sectionTitle = basePlan.Units
                .Select(unit => unit.SectionTitle)
                .FirstOrDefault(title => !string.IsNullOrWhiteSpace(title));

            results.Add(new GeneratedDocumentChunk(
                index,
                content,
                content.Length,
                tokenCount,
                pages.Length == 0 ? null : pages[0],
                pages.Length == 0 ? null : pages[^1],
                sectionTitle,
                combinedUnits.Min(unit => unit.SectionIndex),
                combinedUnits.Max(unit => unit.SectionIndex)));
        }

        return results;
    }

    private List<TextUnit> CreateUnits(
        IReadOnlyList<ChunkSourceSection> sourceSections,
        CancellationToken cancellationToken)
    {
        var units = new List<TextUnit>();

        foreach (var section in sourceSections.OrderBy(section => section.SectionIndex))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var paragraphs = section.Content.Split(
                ["\r\n", "\n", "\r"],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var isFirstUnitInSection = true;

            foreach (var paragraph in paragraphs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (paragraph.Length == 0)
                {
                    continue;
                }

                var paragraphUnits = CreateParagraphUnits(
                    paragraph,
                    section,
                    isFirstUnitInSection,
                    cancellationToken);

                if (paragraphUnits.Count > 0)
                {
                    units.AddRange(paragraphUnits);
                    isFirstUnitInSection = false;
                }
            }
        }

        return units;
    }

    private IReadOnlyList<TextUnit> CreateParagraphUnits(
        string paragraph,
        ChunkSourceSection section,
        bool isFirstUnitInSection,
        CancellationToken cancellationToken)
    {
        const string firstSeparator = "\n\n";

        if (_tokenizer.CountTokens(paragraph) <= _options.MaximumTokens)
        {
            return
            [
                CreateUnit(
                    paragraph,
                    section,
                    firstSeparator,
                    isFirstUnitInSection && !string.IsNullOrWhiteSpace(section.SectionTitle))
            ];
        }

        var units = new List<TextUnit>();
        var sentences = SplitSentences(paragraph);
        var isFirstSentence = true;

        foreach (var sentence in sentences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var separator = isFirstSentence ? firstSeparator : " ";
            var isHeadingStart = isFirstSentence &&
                isFirstUnitInSection &&
                !string.IsNullOrWhiteSpace(section.SectionTitle);

            if (_tokenizer.CountTokens(sentence) <= _options.MaximumTokens)
            {
                units.Add(CreateUnit(
                    sentence,
                    section,
                    separator,
                    isHeadingStart));
            }
            else
            {
                units.AddRange(SplitAtTokenBoundaries(
                    sentence,
                    section,
                    separator,
                    isHeadingStart,
                    cancellationToken));
            }

            isFirstSentence = false;
        }

        return units;
    }

    private IReadOnlyList<TextUnit> SplitAtTokenBoundaries(
        string text,
        ChunkSourceSection section,
        string firstSeparator,
        bool isHeadingStart,
        CancellationToken cancellationToken)
    {
        var units = new List<TextUnit>();
        var remaining = text;
        var separator = firstSeparator;
        var headingStart = isHeadingStart;

        while (_tokenizer.CountTokens(remaining) > _options.MaximumTokens)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tokenIndex = _tokenizer.GetIndexByTokenCount(
                remaining,
                _options.MaximumTokens);
            var splitIndex = FindSafeWordBoundary(remaining, tokenIndex);

            if (splitIndex <= 0 || splitIndex >= remaining.Length)
            {
                splitIndex = AvoidSplittingSurrogatePair(remaining, tokenIndex);
            }

            if (splitIndex <= 0 || splitIndex >= remaining.Length)
            {
                throw new DocumentChunkingException(
                    "The extracted text could not be split within the configured token limit.");
            }

            var content = remaining[..splitIndex].TrimEnd();
            var nextStart = splitIndex;
            while (nextStart < remaining.Length && char.IsWhiteSpace(remaining[nextStart]))
            {
                nextStart++;
            }

            if (content.Length == 0)
            {
                throw new DocumentChunkingException(
                    "Chunk generation produced an empty text segment.");
            }

            units.Add(CreateUnit(content, section, separator, headingStart));
            separator = nextStart > splitIndex ? " " : string.Empty;
            headingStart = false;
            remaining = remaining[nextStart..];
        }

        if (remaining.Length > 0)
        {
            units.Add(CreateUnit(remaining, section, separator, headingStart));
        }

        return units;
    }

    private List<ChunkPlan> CreateBasePlans(
        IReadOnlyList<TextUnit> units,
        CancellationToken cancellationToken)
    {
        var plans = new List<ChunkPlan>();
        var current = new ChunkPlan();

        foreach (var unit in units)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentTokenCount = CountTokens(current.Units);

            if (current.Units.Count > 0 &&
                unit.IsHeadingStart &&
                currentTokenCount >= _options.MinimumTokens)
            {
                plans.Add(current);
                current = new ChunkPlan();
                currentTokenCount = 0;
            }

            var candidate = current.Units.Append(unit).ToArray();
            var candidateTokenCount = CountTokens(candidate);

            if (current.Units.Count > 0 &&
                candidateTokenCount > _options.TargetTokens &&
                currentTokenCount >= _options.MinimumTokens)
            {
                plans.Add(current);
                current = new ChunkPlan();
                candidateTokenCount = CountTokens([unit]);
            }

            if (current.Units.Count > 0 && candidateTokenCount > _options.MaximumTokens)
            {
                plans.Add(current);
                current = new ChunkPlan();
            }

            current.Units.Add(unit);
        }

        if (current.Units.Count > 0)
        {
            plans.Add(current);
        }

        return plans;
    }

    private void RebalanceFinalPlan(IList<ChunkPlan> plans)
    {
        if (plans.Count < 2 || CountTokens(plans[^1].Units) >= _options.MinimumTokens)
        {
            return;
        }

        var previous = plans[^2];
        var final = plans[^1];
        var merged = previous.Units.Concat(final.Units).ToArray();

        if (CountTokens(merged) <= _options.MaximumTokens)
        {
            previous.Units.AddRange(final.Units);
            plans.RemoveAt(plans.Count - 1);
            return;
        }

        while (CountTokens(final.Units) < _options.MinimumTokens && previous.Units.Count > 1)
        {
            var unit = previous.Units[^1];
            var previousCandidate = previous.Units.Take(previous.Units.Count - 1).ToArray();
            var finalCandidate = final.Units.Prepend(unit).ToArray();

            if (CountTokens(previousCandidate) < _options.MinimumTokens ||
                CountTokens(finalCandidate) > _options.MaximumTokens)
            {
                break;
            }

            previous.Units.RemoveAt(previous.Units.Count - 1);
            final.Units.Insert(0, unit);
        }
    }

    private IReadOnlyList<TextUnit> CreateOverlapUnits(
        ChunkPlan previous,
        int tokenBudget)
    {
        if (tokenBudget <= 0)
        {
            return [];
        }

        var overlap = new List<TextUnit>();

        for (var index = previous.Units.Count - 1; index >= 0; index--)
        {
            var unit = previous.Units[index];
            var candidate = overlap.Prepend(unit).ToArray();

            if (CountTokens(candidate) <= tokenBudget)
            {
                overlap.Insert(0, unit);
                continue;
            }

            if (overlap.Count == 0)
            {
                var sliced = SliceUnitFromEnd(unit, tokenBudget);
                if (sliced is not null)
                {
                    overlap.Add(sliced);
                }
            }

            break;
        }

        if (overlap.Count == previous.Units.Count)
        {
            if (overlap.Count > 1)
            {
                overlap.RemoveAt(0);
            }
            else
            {
                var tokenCount = CountTokens(overlap);
                var reducedBudget = Math.Min(tokenBudget, Math.Max(1, tokenCount / 2));
                var sliced = SliceUnitFromEnd(overlap[0], reducedBudget);
                overlap.Clear();
                if (sliced is not null)
                {
                    overlap.Add(sliced);
                }
            }
        }

        return overlap;
    }

    private void TrimOverlapToMaximum(List<TextUnit> combinedUnits, int baseUnitCount)
    {
        while (combinedUnits.Count > baseUnitCount &&
               CountTokens(combinedUnits) > _options.MaximumTokens)
        {
            combinedUnits.RemoveAt(0);
        }
    }

    private TextUnit? SliceUnitFromEnd(TextUnit unit, int tokenBudget)
    {
        if (tokenBudget <= 0)
        {
            return null;
        }

        var index = _tokenizer.GetIndexByTokenCountFromEnd(unit.Text, tokenBudget);
        index = FindSafeWordStart(unit.Text, index);

        if (index <= 0)
        {
            index = _tokenizer.CountTokens(unit.Text) <= tokenBudget
                ? Math.Max(1, unit.Text.Length / 2)
                : _tokenizer.GetIndexByTokenCountFromEnd(unit.Text, tokenBudget);
        }

        index = AvoidSplittingSurrogatePair(unit.Text, index);
        var content = unit.Text[index..].TrimStart();

        return content.Length == 0 || content.Length == unit.Text.Length
            ? null
            : unit with { Text = content, SeparatorBefore = " " };
    }

    private int CountTokens(IEnumerable<TextUnit> units)
    {
        var content = BuildContent(units);
        return content.Length == 0 ? 0 : _tokenizer.CountTokens(content);
    }

    private static string BuildContent(IEnumerable<TextUnit> units)
    {
        var builder = new StringBuilder();

        foreach (var unit in units)
        {
            if (builder.Length > 0)
            {
                builder.Append(unit.SeparatorBefore);
            }

            builder.Append(unit.Text);
        }

        return builder.ToString().Trim();
    }

    private static IReadOnlyList<string> SplitSentences(string text)
    {
        var sentences = new List<string>();
        var start = 0;

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is not ('.' or '!' or '?'))
            {
                continue;
            }

            if (!IsSentenceBoundary(text, index))
            {
                continue;
            }

            var end = index + 1;
            while (end < text.Length && text[end] is '"' or '\'' or ')' or ']' or '}')
            {
                end++;
            }

            var sentence = text[start..end].Trim();
            if (sentence.Length > 0)
            {
                sentences.Add(sentence);
            }

            while (end < text.Length && char.IsWhiteSpace(text[end]))
            {
                end++;
            }

            start = end;
            index = end - 1;
        }

        var remaining = text[start..].Trim();
        if (remaining.Length > 0)
        {
            sentences.Add(remaining);
        }

        return sentences.Count == 0 ? [text] : sentences;
    }

    private static bool IsSentenceBoundary(string text, int punctuationIndex)
    {
        if (punctuationIndex + 1 < text.Length &&
            !char.IsWhiteSpace(text[punctuationIndex + 1]) &&
            text[punctuationIndex + 1] is not ('"' or '\'' or ')' or ']' or '}'))
        {
            return false;
        }

        if (text[punctuationIndex] != '.')
        {
            return true;
        }

        if (punctuationIndex > 0 &&
            punctuationIndex + 1 < text.Length &&
            char.IsDigit(text[punctuationIndex - 1]) &&
            char.IsDigit(text[punctuationIndex + 1]))
        {
            return false;
        }

        var start = punctuationIndex - 1;
        while (start >= 0 && (char.IsLetter(text[start]) || text[start] == '.'))
        {
            start--;
        }

        var token = text[(start + 1)..punctuationIndex].Trim('.');

        return token.Length > 1 && !ProtectedAbbreviations.Contains(token);
    }

    private static int FindSafeWordBoundary(string text, int requestedIndex)
    {
        var index = AvoidSplittingSurrogatePair(text, requestedIndex);

        if (index <= 0 || index >= text.Length ||
            char.IsWhiteSpace(text[index - 1]) ||
            char.IsWhiteSpace(text[index]))
        {
            return index;
        }

        var boundary = index;
        while (boundary > 0 && !char.IsWhiteSpace(text[boundary - 1]))
        {
            boundary--;
        }

        return boundary > 0 ? boundary : index;
    }

    private static int FindSafeWordStart(string text, int requestedIndex)
    {
        var index = AvoidSplittingSurrogatePair(text, requestedIndex);

        while (index < text.Length && !char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }

    private static int AvoidSplittingSurrogatePair(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);

        return index > 0 &&
               index < text.Length &&
               char.IsHighSurrogate(text[index - 1]) &&
               char.IsLowSurrogate(text[index])
            ? index - 1
            : index;
    }

    private static TextUnit CreateUnit(
        string text,
        ChunkSourceSection section,
        string separatorBefore,
        bool isHeadingStart) =>
        new(
            text,
            separatorBefore,
            section.SectionIndex,
            section.PageNumber,
            section.SectionTitle,
            isHeadingStart);

    private sealed class ChunkPlan
    {
        public List<TextUnit> Units { get; } = [];
    }

    private sealed record TextUnit(
        string Text,
        string SeparatorBefore,
        int SectionIndex,
        int? PageNumber,
        string? SectionTitle,
        bool IsHeadingStart);
}
