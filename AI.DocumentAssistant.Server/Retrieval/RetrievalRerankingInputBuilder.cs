using AI.DocumentAssistant.Server.Chunking;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Retrieval;

public sealed class RetrievalRerankingInputBuilder
    : IRetrievalRerankingInputBuilder
{
    private const int MaximumDocumentNameCharacters = 300;
    private const int MaximumHeadingCharacters = 500;

    private readonly IDocumentTokenizer _tokenizer;
    private readonly RetrievalRerankingOptions _options;

    public RetrievalRerankingInputBuilder(
        IDocumentTokenizer tokenizer,
        IOptions<RetrievalRerankingOptions> options)
    {
        _tokenizer = tokenizer;
        _options = options.Value;
    }

    public RetrievalRerankingRequest Build(
        string question,
        IReadOnlyList<RetrievedDocumentChunk> hybridCandidates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(hybridCandidates);

        var candidateLimit = Math.Clamp(
            Math.Min(_options.CandidateCount, _options.MaxCandidateCount),
            2,
            RetrievalRerankingLimits.MaximumCandidateCount);
        var maximumInputTokens = Math.Clamp(
            _options.MaxInputTokens,
            RetrievalRerankingLimits.MinimumMaxInputTokens,
            RetrievalRerankingLimits.MaximumMaxInputTokens);
        var maximumCandidateTokens = Math.Min(
            Math.Clamp(
                _options.MaxCandidateTokens,
                RetrievalRerankingLimits.MinimumMaxCandidateTokens,
                RetrievalRerankingLimits.MaximumMaxCandidateTokens),
            maximumInputTokens);
        var candidates = new List<RetrievalRerankingCandidate>(candidateLimit);

        foreach (var chunk in hybridCandidates.Take(candidateLimit))
        {
            var candidateId = $"C{candidates.Count + 1}";
            var candidate = BuildCandidate(
                candidateId,
                chunk,
                maximumCandidateTokens);
            if (candidate is null)
            {
                break;
            }

            var trialCandidates = candidates.Append(candidate).ToArray();
            var trialTokens = CountInputTokens(question, trialCandidates);
            var candidateTokenLimit = maximumCandidateTokens;

            while (trialTokens > maximumInputTokens)
            {
                var reduction = Math.Max(
                    1,
                    trialTokens - maximumInputTokens + 4);
                candidateTokenLimit -= reduction;
                if (candidateTokenLimit <
                    RetrievalRerankingLimits.MinimumMaxCandidateTokens)
                {
                    candidate = null;
                    break;
                }

                candidate = BuildCandidate(candidateId, chunk, candidateTokenLimit);
                if (candidate is null)
                {
                    break;
                }

                trialCandidates = candidates.Append(candidate).ToArray();
                trialTokens = CountInputTokens(question, trialCandidates);
            }

            if (candidate is null)
            {
                break;
            }

            candidates.Add(candidate);
        }

        var inputTokenCount = CountInputTokens(question, candidates);
        return new RetrievalRerankingRequest(
            question,
            candidates.ToArray(),
            inputTokenCount);
    }

    private RetrievalRerankingCandidate? BuildCandidate(
        string candidateId,
        RetrievedDocumentChunk chunk,
        int maximumTokens)
    {
        var content = chunk.Content.Trim();
        if (content.Length == 0)
        {
            return null;
        }

        var candidate = new RetrievalRerankingCandidate(
            candidateId,
            chunk.ChunkId,
            TruncateCharacters(chunk.DocumentName, MaximumDocumentNameCharacters),
            FormatPageLabel(chunk.PageStart, chunk.PageEnd),
            string.IsNullOrWhiteSpace(chunk.Heading)
                ? null
                : TruncateCharacters(chunk.Heading.Trim(), MaximumHeadingCharacters),
            content,
            0);
        var candidateTokens = CountCandidateTokens(candidate);
        if (candidateTokens <= maximumTokens)
        {
            return candidate with { ApproximateTokenCount = candidateTokens };
        }

        var emptyCandidate = candidate with { Content = string.Empty };
        var framingTokens = CountCandidateTokens(emptyCandidate);
        var availableContentTokens = maximumTokens - framingTokens - 2;
        if (availableContentTokens <= 0)
        {
            return null;
        }

        while (availableContentTokens > 0)
        {
            var endIndex = ClampBoundary(
                content,
                _tokenizer.GetIndexByTokenCount(content, availableContentTokens));
            if (endIndex <= 0)
            {
                return null;
            }

            var truncatedContent = content[..endIndex].TrimEnd();
            if (endIndex < content.Length)
            {
                truncatedContent += "\n[Candidate content truncated]";
            }

            candidate = candidate with { Content = truncatedContent };
            candidateTokens = CountCandidateTokens(candidate);
            if (candidateTokens <= maximumTokens)
            {
                return candidate with { ApproximateTokenCount = candidateTokens };
            }

            availableContentTokens -= Math.Max(1, candidateTokens - maximumTokens + 2);
        }

        return null;
    }

    private int CountInputTokens(
        string question,
        IReadOnlyList<RetrievalRerankingCandidate> candidates) =>
        _tokenizer.CountTokens(
            RetrievalRerankingPrompt.BuildUserInput(question, candidates));

    private int CountCandidateTokens(RetrievalRerankingCandidate candidate) =>
        _tokenizer.CountTokens(
            RetrievalRerankingPrompt.SerializeCandidate(candidate));

    private static string FormatPageLabel(int? pageStart, int? pageEnd) =>
        (pageStart, pageEnd) switch
        {
            (null, null) => "Unavailable",
            (int start, null) => start.ToString(),
            (null, int end) => end.ToString(),
            (int start, int end) when start == end => start.ToString(),
            (int start, int end) => $"{start}-{end}"
        };

    private static string TruncateCharacters(string value, int maximumCharacters)
    {
        if (value.Length <= maximumCharacters)
        {
            return value;
        }

        var length = maximumCharacters;
        if (char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value[..length].TrimEnd() + "…";
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
