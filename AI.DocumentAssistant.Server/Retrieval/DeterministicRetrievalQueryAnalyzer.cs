using System.Text;
using System.Text.RegularExpressions;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Understanding;

namespace AI.DocumentAssistant.Server.Retrieval;

public sealed partial class DeterministicRetrievalQueryAnalyzer
    : IRetrievalQueryAnalyzer
{
    private const int MaximumSearchTerms = 12;
    private const int MaximumStructuredValues = 8;

    private static readonly HashSet<string> StopWords = new(
        [
            "a", "about", "and", "are", "ce", "care", "cu", "de", "despre",
            "do", "does", "este", "find", "for", "give", "if", "in", "is",
            "la", "mentioning", "must", "of", "or", "provide", "say", "sunt",
            "the", "to", "un", "what", "when", "with", "și", "sau"
        ],
        StringComparer.Ordinal);

    private static readonly (DocumentType Type, string[] Aliases)[] TypeAliases =
    [
        (DocumentType.Contract, ["contract", "contractul", "contracte"]),
        (DocumentType.Invoice, ["invoice", "factura", "factură", "facturi"]),
        (DocumentType.Report, ["report", "raport"]),
        (DocumentType.Policy, ["policy", "politică"]),
        (DocumentType.Procedure, ["procedure", "procedură"]),
        (DocumentType.Manual, ["manual"]),
        (DocumentType.Form, ["form", "formular"]),
        (DocumentType.Letter, ["letter", "scrisoare"]),
        (DocumentType.Resume, ["resume", "cv"]),
        (DocumentType.ResearchPaper, ["research paper"]),
        (DocumentType.CourseMaterial, ["course", "curs"])
    ];

    public RetrievalQuery Analyze(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var originalText = query.Trim();
        var normalizedText = NormalizeText(originalText);
        var words = WordPattern().Matches(normalizedText)
            .Select(match => match.Value)
            .ToArray();
        var wordSet = words.ToHashSet(StringComparer.Ordinal);
        var normalizedWordSequence = $" {string.Join(' ', words)} ";

        var searchTerms = words
            .Where(word => word.Length >= 2 && !StopWords.Contains(word))
            .Distinct(StringComparer.Ordinal)
            .Take(MaximumSearchTerms)
            .ToArray();
        var documentTypes = TypeAliases
            .Where(entry => entry.Aliases.Any(alias =>
                AliasMatches(alias, wordSet, normalizedWordSequence)))
            .Select(entry => entry.Type)
            .Distinct()
            .ToArray();
        var identifiers = IdentifierPattern().Matches(originalText)
            .Select(match => NormalizeText(match.Value))
            .Where(value => value.Length <= 64 &&
                value.Any(char.IsLetter) &&
                value.Any(char.IsDigit))
            .Distinct(StringComparer.Ordinal)
            .Take(MaximumStructuredValues)
            .ToArray();
        var dates = DatePattern().Matches(originalText)
            .Select(match => DocumentMetadataNormalizer.TryNormalizeDate(match.Value))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Take(MaximumStructuredValues)
            .ToArray();
        var monetaryValues = MonetaryPattern().Matches(originalText)
            .Select(match => NormalizeMonetaryValue(
                match.Groups["amount"].Value,
                match.Groups["currency"].Value))
            .Distinct(StringComparer.Ordinal)
            .Take(MaximumStructuredValues)
            .ToArray();

        return new RetrievalQuery(
            originalText,
            normalizedText,
            searchTerms,
            documentTypes,
            identifiers,
            dates,
            monetaryValues);
    }

    private static bool AliasMatches(
        string alias,
        IReadOnlySet<string> words,
        string normalizedWordSequence) =>
        alias.Contains(' ', StringComparison.Ordinal)
            ? normalizedWordSequence.Contains($" {alias} ", StringComparison.Ordinal)
            : words.Contains(alias);

    private static string NormalizeText(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        var builder = new StringBuilder(normalized.Length);
        var pendingSpace = false;

        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static string NormalizeMonetaryValue(string amount, string currency)
    {
        var combined = string.Concat(amount, currency).Normalize(NormalizationForm.FormKC);
        return string.Concat(combined.Where(char.IsLetterOrDigit)).ToUpperInvariant();
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])[\p{L}\p{N}]+(?:[-/_][\p{L}\p{N}]+)+(?![\p{L}\p{N}])",
        RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(
        @"(?<!\d)(?:\d{4}[-/.]\d{1,2}[-/.]\d{1,2}|\d{1,2}\.\d{1,2}\.\d{4})(?!\d)",
        RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();

    [GeneratedRegex(
        @"(?<!\d)(?<amount>\d[\d.,\s]*\d|\d)\s*(?<currency>EUR|USD|RON|GBP|CHF|CAD|AUD|JPY|CNY)(?![A-Za-z])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MonetaryPattern();
}
