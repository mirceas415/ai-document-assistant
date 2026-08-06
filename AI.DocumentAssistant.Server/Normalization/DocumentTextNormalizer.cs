using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Normalization;

public sealed partial class DocumentTextNormalizer : IDocumentTextNormalizer
{
    private readonly DocumentNormalizationOptions _options;

    public DocumentTextNormalizer(IOptions<DocumentNormalizationOptions> options)
    {
        _options = options.Value;
    }

    private BoilerplateDetection DetectBoilerplate(
        IReadOnlyList<PreparedSection> sections,
        int requiredOccurrences,
        CancellationToken cancellationToken)
    {
        var repeatedHeaderLineKeys = FindRepeatedCandidateKeys(
            sections,
            _options.HeaderCandidateLineCount,
            fromStart: true,
            requiredOccurrences);
        var repeatedFooterLineKeys = FindRepeatedCandidateKeys(
            sections,
            _options.FooterCandidateLineCount,
            fromStart: false,
            requiredOccurrences);
        var blockCandidates = new List<CandidateBlock>();

        for (var pageOrdinal = 0; pageOrdinal < sections.Count; pageOrdinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var section = sections[pageOrdinal];
            blockCandidates.AddRange(CreateBlockCandidates(
                section,
                pageOrdinal,
                _options.HeaderCandidateLineCount,
                isHeader: true));
            blockCandidates.AddRange(CreateBlockCandidates(
                section,
                pageOrdinal,
                _options.FooterCandidateLineCount,
                isHeader: false));
        }

        var confirmedHeaderKeys = FindConfirmedBlockKeys(
            blockCandidates.Where(candidate => candidate.IsHeader),
            requiredOccurrences);
        var confirmedFooterKeys = FindConfirmedBlockKeys(
            blockCandidates.Where(candidate => !candidate.IsHeader),
            requiredOccurrences);
        var lineIndexesBySection = new Dictionary<int, HashSet<int>>();
        var selectedBlockKeys = new HashSet<(bool IsHeader, string Key)>();

        foreach (var pageRegion in blockCandidates.GroupBy(candidate =>
                     (candidate.SectionIndex, candidate.IsHeader)))
        {
            var confirmedKeys = pageRegion.Key.IsHeader
                ? confirmedHeaderKeys
                : confirmedFooterKeys;
            var coveredIndexes = new HashSet<int>();

            foreach (var candidate in pageRegion
                         .Where(candidate => confirmedKeys.Contains(candidate.Key))
                         .OrderByDescending(candidate => candidate.OriginalLineIndexes.Length)
                         .ThenByDescending(candidate => candidate.Key.Length))
            {
                if (candidate.OriginalLineIndexes.All(coveredIndexes.Contains))
                {
                    continue;
                }

                if (!lineIndexesBySection.TryGetValue(
                        candidate.SectionIndex,
                        out var sectionIndexes))
                {
                    sectionIndexes = [];
                    lineIndexesBySection[candidate.SectionIndex] = sectionIndexes;
                }

                foreach (var index in candidate.OriginalLineIndexes)
                {
                    coveredIndexes.Add(index);
                    sectionIndexes.Add(index);
                }

                selectedBlockKeys.Add((candidate.IsHeader, candidate.Key));
            }
        }

        return new BoilerplateDetection(
            repeatedHeaderLineKeys,
            repeatedFooterLineKeys,
            lineIndexesBySection,
            blockCandidates.Count,
            selectedBlockKeys.Count);
    }

    private IReadOnlyList<CandidateBlock> CreateBlockCandidates(
        PreparedSection section,
        int pageOrdinal,
        int lineCount,
        bool isHeader)
    {
        var totalNonEmptyLineCount = section.Lines.Count(line =>
            !string.IsNullOrWhiteSpace(line));
        if (totalNonEmptyLineCount <= 2)
        {
            return [];
        }

        var edgeIndexes = GetCandidateIndexes(
            section.Lines,
            lineCount,
            fromStart: isHeader);
        if (edgeIndexes.Count < 2)
        {
            return [];
        }

        var candidatesByKey = new Dictionary<string, CandidateBlock>(StringComparer.Ordinal);
        for (var start = 0; start < edgeIndexes.Count - 1; start++)
        {
            for (var end = start + 1; end < edgeIndexes.Count; end++)
            {
                var boundaryOffset = isHeader
                    ? start
                    : edgeIndexes.Count - 1 - end;
                if (boundaryOffset > _options.MaximumBlockBoundaryLineOffset)
                {
                    continue;
                }

                var originalLineIndexes = edgeIndexes
                    .Skip(start)
                    .Take(end - start + 1)
                    .ToArray();
                var comparisonLines = originalLineIndexes
                    .Select(index => section.Lines[index])
                    .Where(line => !IsStandalonePageNumber(
                        line,
                        section.Source.PageNumber))
                    .ToArray();

                if (comparisonLines.Length < 2)
                {
                    continue;
                }

                var key = CreateBlockComparisonKey(comparisonLines);
                if (key.Length < _options.MinimumCandidateBlockLength ||
                    key.Length > _options.MaximumCandidateLength)
                {
                    continue;
                }

                var candidate = new CandidateBlock(
                    pageOrdinal,
                    section.Source.SectionIndex,
                    isHeader,
                    key,
                    originalLineIndexes);
                if (!candidatesByKey.TryGetValue(key, out var existing) ||
                    existing.OriginalLineIndexes.Length < originalLineIndexes.Length)
                {
                    candidatesByKey[key] = candidate;
                }
            }
        }

        return candidatesByKey.Values.ToArray();
    }

    private HashSet<string> FindConfirmedBlockKeys(
        IEnumerable<CandidateBlock> candidates,
        int requiredOccurrences)
    {
        var confirmed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidateGroup in candidates.GroupBy(
                     candidate => candidate.Key,
                     StringComparer.Ordinal))
        {
            var pageOrdinals = candidateGroup
                .Select(candidate => candidate.PageOrdinal)
                .Distinct()
                .Order()
                .ToArray();
            if (pageOrdinals.Length >= requiredOccurrences ||
                (candidateGroup.Key.Length >= _options.MinimumLocalCandidateBlockLength &&
                 HasDenseLocalOccurrence(pageOrdinals)))
            {
                confirmed.Add(candidateGroup.Key);
            }
        }

        return confirmed;
    }

    private bool HasDenseLocalOccurrence(IReadOnlyList<int> pageOrdinals)
    {
        var minimumCount = _options.MinimumPageCountForBoilerplateDetection;
        if (pageOrdinals.Count < minimumCount)
        {
            return false;
        }

        for (var start = 0; start <= pageOrdinals.Count - minimumCount; start++)
        {
            var end = start + minimumCount - 1;
            var pageSpan = pageOrdinals[end] - pageOrdinals[start] + 1;
            if ((double)minimumCount / pageSpan >= _options.MinimumPageOccurrenceRatio)
            {
                return true;
            }
        }

        return false;
    }

    public DocumentNormalizationResult Normalize(
        IReadOnlyList<NormalizationSourceSection> sections,
        bool isPdf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sections);

        var orderedSections = sections
            .OrderBy(section => section.SectionIndex)
            .ToArray();
        var prepared = orderedSections
            .Select(section => new PreparedSection(
                section,
                NormalizeWhitespace(section.Content).Split('\n').ToList()))
            .ToArray();

        var pdfSections = isPdf
            ? prepared.Where(section => section.Source.PageNumber.HasValue).ToArray()
            : [];
        var detection = BoilerplateDetection.Empty;

        if (pdfSections.Length >= _options.MinimumPageCountForBoilerplateDetection)
        {
            var requiredOccurrences = (int)Math.Ceiling(
                pdfSections.Length * _options.MinimumPageOccurrenceRatio);
            detection = DetectBoilerplate(
                pdfSections,
                requiredOccurrences,
                cancellationToken);
        }

        var normalizedSections = new List<NormalizedTextSection>(prepared.Length);

        foreach (var section in prepared)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lines = section.Lines.ToArray();
            if (isPdf && section.Source.PageNumber.HasValue)
            {
                var headerIndexes = GetCandidateIndexes(
                    lines,
                    _options.HeaderCandidateLineCount,
                    fromStart: true);
                var footerIndexes = GetCandidateIndexes(
                    lines,
                    _options.FooterCandidateLineCount,
                    fromStart: false);

                RemoveRegionLines(
                    lines,
                    GetLineMatchIndexes(headerIndexes, fromStart: true),
                    detection.RepeatedHeaderLineKeys);
                RemoveRegionLines(
                    lines,
                    GetLineMatchIndexes(footerIndexes, fromStart: false),
                    detection.RepeatedFooterLineKeys);

                if (detection.BlockLineIndexesBySection.TryGetValue(
                        section.Source.SectionIndex,
                        out var blockLineIndexes))
                {
                    foreach (var index in blockLineIndexes)
                    {
                        var isPageNumber = IsStandalonePageNumber(
                            lines[index],
                            section.Source.PageNumber);
                        if (!IsProtectedNumberedHeading(lines[index]) &&
                            (_options.EnablePageNumberRemoval || !isPageNumber))
                        {
                            lines[index] = string.Empty;
                        }
                    }
                }

                if (_options.EnablePageNumberRemoval)
                {
                    foreach (var index in headerIndexes.Concat(footerIndexes).Distinct())
                    {
                        if (IsStandalonePageNumber(lines[index], section.Source.PageNumber))
                        {
                            lines[index] = string.Empty;
                        }
                    }
                }
            }

            var normalized = CollapseBlankLines(lines);
            if (_options.EnableWordBreakRepair)
            {
                normalized = RepairSafeWordBreaks(normalized);
            }

            normalized = NormalizeWhitespace(normalized);
            normalizedSections.Add(CreateResult(section.Source, normalized));
        }

        if (normalizedSections.Count > 0 &&
            normalizedSections.All(section => string.IsNullOrWhiteSpace(section.Content)) &&
            prepared.Any(section => section.Lines.Any(line =>
                !string.IsNullOrWhiteSpace(line) && !IsStandalonePageNumber(line))))
        {
            normalizedSections.Clear();
            normalizedSections.AddRange(prepared.Select(section =>
                CreateResult(section.Source, NormalizeWhitespace(section.Source.Content))));
        }

        var originalCharacterCount = orderedSections.Sum(section => (long)section.Content.Length);
        var normalizedCharacterCount = normalizedSections.Sum(section => (long)section.Content.Length);

        return new DocumentNormalizationResult(
            normalizedSections,
            originalCharacterCount,
            normalizedCharacterCount,
            normalizedSections.Count(section => section.Changed),
            normalizedSections.Sum(section => (long)section.RemovedCharacterCount),
            pdfSections.Length,
            detection.CandidateBlockCount,
            detection.ConfirmedRepeatedBlockCount);
    }

    private HashSet<string> FindRepeatedCandidateKeys(
        IReadOnlyList<PreparedSection> sections,
        int lineCount,
        bool fromStart,
        int requiredOccurrences)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var section in sections)
        {
            if (section.Lines.Count(line => !string.IsNullOrWhiteSpace(line)) <= 1)
            {
                continue;
            }

            var regionIndexes = GetCandidateIndexes(section.Lines, lineCount, fromStart);
            var pageKeys = GetLineMatchIndexes(regionIndexes, fromStart)
                .Select(index => CreateComparisonKey(section.Lines[index]))
                .Where(key => key.Length > 0 && key.Length <= _options.MaximumCandidateLength)
                .Where(key => !IsStandalonePageNumber(key))
                .Where(key => !IsProtectedNumberedHeading(key))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var key in pageKeys)
            {
                occurrences[key] = occurrences.GetValueOrDefault(key) + 1;
            }
        }

        return occurrences
            .Where(entry => entry.Value >= requiredOccurrences)
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static void RemoveRegionLines(
        IList<string> lines,
        IEnumerable<int> indexes,
        IReadOnlySet<string> repeatedKeys)
    {
        foreach (var index in indexes)
        {
            if (repeatedKeys.Contains(CreateComparisonKey(lines[index])))
            {
                lines[index] = string.Empty;
            }
        }
    }

    private IReadOnlyList<int> GetLineMatchIndexes(
        IReadOnlyList<int> regionIndexes,
        bool fromStart)
    {
        var maximumLineCount = _options.MaximumBlockBoundaryLineOffset + 1;
        return (fromStart
                ? regionIndexes.Take(maximumLineCount)
                : regionIndexes.TakeLast(maximumLineCount))
            .ToArray();
    }

    private IReadOnlyList<int> GetCandidateIndexes(
        IReadOnlyList<string> lines,
        int lineCount,
        bool fromStart)
    {
        var nonEmptyIndexes = lines
            .Select((line, index) => (line, index))
            .Where(item => !string.IsNullOrWhiteSpace(item.line))
            .Select(item => item.index)
            .ToArray();
        var effectiveLineCount = GetEffectiveRegionLineCount(
            nonEmptyIndexes.Length,
            lineCount,
            fromStart
                ? _options.FooterCandidateLineCount
                : _options.HeaderCandidateLineCount);

        return (fromStart
                ? nonEmptyIndexes.Take(effectiveLineCount)
                : nonEmptyIndexes.Reverse().Take(effectiveLineCount).Reverse())
            .ToArray();
    }

    private static int GetEffectiveRegionLineCount(
        int availableLineCount,
        int requestedLineCount,
        int oppositeRequestedLineCount)
    {
        if (availableLineCount <= 3 ||
            requestedLineCount + oppositeRequestedLineCount <= availableLineCount)
        {
            return Math.Min(requestedLineCount, availableLineCount);
        }

        var edgeCapacity = availableLineCount - 1;
        var requestedTotal = requestedLineCount + oppositeRequestedLineCount;
        var proportionalCount = (int)Math.Floor(
            edgeCapacity * ((double)requestedLineCount / requestedTotal));

        return Math.Clamp(proportionalCount, 1, edgeCapacity - 1);
    }

    private static string NormalizeWhitespace(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var normalizedLineEndings = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalizedLineEndings
            .Split('\n')
            .Select(line => HorizontalWhitespaceRegex().Replace(line, " ").Trim());

        return CollapseBlankLines(lines);
    }

    private static string CollapseBlankLines(IEnumerable<string> lines)
    {
        var result = new StringBuilder();
        var hasContent = false;
        var pendingBlankLine = false;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (hasContent)
                {
                    pendingBlankLine = true;
                }

                continue;
            }

            if (hasContent)
            {
                result.Append(pendingBlankLine ? "\n\n" : "\n");
            }

            result.Append(line.Trim());
            hasContent = true;
            pendingBlankLine = false;
        }

        return result.ToString();
    }

    private static string RepairSafeWordBreaks(string content)
    {
        if (content.Length == 0)
        {
            return content;
        }

        var lines = content.Split('\n').ToList();
        var index = 0;

        while (index < lines.Count - 1)
        {
            var current = lines[index];
            var next = lines[index + 1];

            if (current.Length > 0 && next.Length > 0 &&
                TryGetSafeWordBreak(current, next, out var joined))
            {
                lines[index] = joined;
                lines.RemoveAt(index + 1);
                continue;
            }

            index++;
        }

        return string.Join('\n', lines);
    }

    private static bool TryGetSafeWordBreak(
        string current,
        string next,
        out string joined)
    {
        joined = string.Empty;
        if (!current.EndsWith("-", StringComparison.Ordinal) ||
            current.EndsWith("--", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(next) ||
            !char.IsLower(next[0]))
        {
            return false;
        }

        var prefixStart = current.Length - 2;
        while (prefixStart >= 0 && char.IsLetter(current[prefixStart]))
        {
            prefixStart--;
        }

        prefixStart++;
        var prefixLength = current.Length - 1 - prefixStart;
        var suffixLength = 0;
        while (suffixLength < next.Length && char.IsLetter(next[suffixLength]))
        {
            suffixLength++;
        }

        if (prefixLength < 4 || suffixLength < 3 ||
            (suffixLength < next.Length && next[suffixLength] == '-'))
        {
            return false;
        }

        joined = current[..^1] + next;
        return true;
    }

    private static NormalizedTextSection CreateResult(
        NormalizationSourceSection source,
        string normalized) =>
        new(
            source.SectionIndex,
            normalized,
            source.PageNumber,
            source.SectionTitle,
            !string.Equals(source.Content, normalized, StringComparison.Ordinal),
            Math.Max(0, source.Content.Length - normalized.Length));

    private static string CreateComparisonKey(string line) =>
        CanonicalizeComparisonText(line);

    private static string CreateBlockComparisonKey(IEnumerable<string> lines)
    {
        var repaired = RepairSafeWordBreaks(string.Join('\n', lines));
        return CanonicalizeComparisonText(repaired.Replace('\n', ' '));
    }

    private static string CanonicalizeComparisonText(string value)
    {
        var normalized = HorizontalWhitespaceRegex().Replace(
            value.Normalize(NormalizationForm.FormKC).Trim(),
            " ");
        normalized = SpaceBeforeClosingPunctuationRegex().Replace(normalized, "$1");
        normalized = SpaceAfterOpeningPunctuationRegex().Replace(normalized, "$1");
        return normalized.ToUpperInvariant();
    }

    private static bool IsStandalonePageNumber(string line, int? expectedPageNumber = null)
    {
        var match = StandalonePageNumberRegex().Match(line.Trim());
        if (!match.Success || expectedPageNumber is null)
        {
            return match.Success;
        }

        return int.TryParse(match.Groups["current"].Value, out var displayedPageNumber) &&
               displayedPageNumber == expectedPageNumber;
    }

    private static bool IsProtectedNumberedHeading(string line)
    {
        return NumberedHeadingRegex().IsMatch(line.Trim());
    }

    [GeneratedRegex(@"[^\S\r\n]+", RegexOptions.CultureInvariant)]
    private static partial Regex HorizontalWhitespaceRegex();

    [GeneratedRegex(@"\s+([,.;:!?%\)\]\}])", RegexOptions.CultureInvariant)]
    private static partial Regex SpaceBeforeClosingPunctuationRegex();

    [GeneratedRegex(@"([\(\[\{])\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SpaceAfterOpeningPunctuationRegex();

    [GeneratedRegex(
        @"^(?:(?:page|pagina)\s+(?<current>\d+)(?:\s*(?:/|of|din)\s*\d+)?|(?<current>\d+)\s*/\s*\d+|(?<current>\d+))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StandalonePageNumberRegex();

    [GeneratedRegex(
        @"^(?<number>\d+(?:\.\d+)*)(?<marker>[.)]?)\s+(?<title>\p{L}.*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex NumberedHeadingRegex();

    private sealed record PreparedSection(
        NormalizationSourceSection Source,
        List<string> Lines);

    private sealed record CandidateBlock(
        int PageOrdinal,
        int SectionIndex,
        bool IsHeader,
        string Key,
        int[] OriginalLineIndexes);

    private sealed record BoilerplateDetection(
        HashSet<string> RepeatedHeaderLineKeys,
        HashSet<string> RepeatedFooterLineKeys,
        Dictionary<int, HashSet<int>> BlockLineIndexesBySection,
        int CandidateBlockCount,
        int ConfirmedRepeatedBlockCount)
    {
        public static BoilerplateDetection Empty { get; } = new(
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            new Dictionary<int, HashSet<int>>(),
            0,
            0);
    }
}
