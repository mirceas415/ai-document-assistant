using System.Text;
using AI.DocumentAssistant.Server.Chunking;

namespace AI.DocumentAssistant.Server.Understanding;

public sealed class DocumentUnderstandingInputBuilder : IDocumentUnderstandingInputBuilder
{
    private const int SamplingSafetyReserveTokens = 128;
    private const string BeginningMarker = "[Representative sample: beginning]";
    private const string MiddleMarker = "[Representative sample: middle]";
    private const string EndMarker = "[Representative sample: end]";

    private readonly IDocumentTokenizer _tokenizer;

    public DocumentUnderstandingInputBuilder(IDocumentTokenizer tokenizer)
    {
        _tokenizer = tokenizer;
    }

    public DocumentUnderstandingInput Build(
        IReadOnlyList<DocumentUnderstandingSourceSection> sourceSections,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceSections);

        var orderedSections = sourceSections
            .OrderBy(section => section.SectionIndex)
            .Where(section => !string.IsNullOrWhiteSpace(section.NormalizedContent))
            .ToArray();

        var canonicalBuilder = new StringBuilder();
        var annotatedBuilder = new StringBuilder();

        foreach (var section in orderedSections)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (canonicalBuilder.Length > 0)
            {
                canonicalBuilder.Append("\n\n");
                annotatedBuilder.Append("\n\n");
            }

            var content = section.NormalizedContent.Trim();
            canonicalBuilder.Append(content);
            AppendProvenance(annotatedBuilder, section);
            annotatedBuilder.Append(content);
        }

        var canonicalContent = canonicalBuilder.ToString();
        var annotatedContent = annotatedBuilder.ToString();
        var sourceContentHash = DocumentUnderstandingContentHasher.Compute(canonicalContent);
        var fullTokenCount = canonicalContent.Length == 0
            ? 0
            : _tokenizer.CountTokens(canonicalContent);
        var meaningfulCharacterCount = canonicalContent.Count(char.IsLetterOrDigit);
        var hasSufficientText =
            fullTokenCount >= DocumentUnderstandingLimits.MinimumUsableTokens &&
            meaningfulCharacterCount >= DocumentUnderstandingLimits.MinimumMeaningfulCharacters;

        if (!hasSufficientText)
        {
            return new DocumentUnderstandingInput(
                string.Empty,
                sourceContentHash,
                fullTokenCount,
                0,
                false,
                false,
                DocumentUnderstandingArchitecture.InsufficientTextReason);
        }

        var annotatedTokenCount = _tokenizer.CountTokens(annotatedContent);
        if (annotatedTokenCount <= DocumentUnderstandingLimits.MaximumInputTokens)
        {
            return new DocumentUnderstandingInput(
                annotatedContent,
                sourceContentHash,
                fullTokenCount,
                annotatedTokenCount,
                false,
                true,
                null);
        }

        var sampledContent = BuildRepresentativeSample(annotatedContent);
        var sampledTokenCount = _tokenizer.CountTokens(sampledContent);
        if (sampledTokenCount > DocumentUnderstandingLimits.MaximumInputTokens)
        {
            throw new InvalidOperationException(
                "Document understanding sampling exceeded its token budget.");
        }

        return new DocumentUnderstandingInput(
            sampledContent,
            sourceContentHash,
            fullTokenCount,
            sampledTokenCount,
            true,
            true,
            null);
    }

    private string BuildRepresentativeSample(string content)
    {
        var scaffold = $"{BeginningMarker}\n\n\n\n{MiddleMarker}\n\n\n\n{EndMarker}\n\n";
        var scaffoldTokens = _tokenizer.CountTokens(scaffold);
        var availableTokens = DocumentUnderstandingLimits.MaximumInputTokens -
            scaffoldTokens - SamplingSafetyReserveTokens;
        if (availableTokens < 3)
        {
            throw new InvalidOperationException(
                "Document understanding sampling has no available content budget.");
        }

        var beginningBudget = availableTokens / 2;
        var middleBudget = availableTokens / 4;
        var endBudget = availableTokens - beginningBudget - middleBudget;

        var beginningEnd = ClampBoundary(
            content,
            _tokenizer.GetIndexByTokenCount(content, beginningBudget));
        var endStart = ClampBoundary(
            content,
            _tokenizer.GetIndexByTokenCountFromEnd(content, endBudget));

        beginningEnd = Math.Min(beginningEnd, endStart);
        var middleAvailableStart = beginningEnd;
        var middleAvailableEnd = Math.Max(beginningEnd, endStart);
        var midpoint = middleAvailableStart +
            ((middleAvailableEnd - middleAvailableStart) / 2);

        var leftHalf = content[middleAvailableStart..midpoint];
        var rightHalf = content[midpoint..middleAvailableEnd];
        var leftBudget = middleBudget / 2;
        var rightBudget = middleBudget - leftBudget;
        var middleStart = middleAvailableStart + ClampBoundary(
            leftHalf,
            _tokenizer.GetIndexByTokenCountFromEnd(leftHalf, leftBudget));
        var middleEnd = midpoint + ClampBoundary(
            rightHalf,
            _tokenizer.GetIndexByTokenCount(rightHalf, rightBudget));

        var beginning = content[..beginningEnd].Trim();
        var middle = content[middleStart..middleEnd].Trim();
        var end = content[endStart..].Trim();

        return $"{BeginningMarker}\n\n{beginning}\n\n{MiddleMarker}\n\n{middle}\n\n{EndMarker}\n\n{end}";
    }

    private static void AppendProvenance(
        StringBuilder builder,
        DocumentUnderstandingSourceSection section)
    {
        if (section.PageNumber is not null)
        {
            builder.Append("[Page ");
            builder.Append(section.PageNumber.Value);
            builder.AppendLine("]");
        }

        if (!string.IsNullOrWhiteSpace(section.SectionTitle))
        {
            builder.Append("[Heading: ");
            builder.Append(CollapseWhitespace(section.SectionTitle));
            builder.AppendLine("]");
        }
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value.Trim())
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

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static int ClampBoundary(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        return index > 0 &&
               index < text.Length &&
               char.IsHighSurrogate(text[index - 1]) &&
               char.IsLowSurrogate(text[index])
            ? index - 1
            : index;
    }
}
