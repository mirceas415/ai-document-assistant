using System.Text;
using AI.DocumentAssistant.Server.Chunking;
using AI.DocumentAssistant.Server.Retrieval;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Rag;

public sealed class RagContextBuilder : IRagContextBuilder
{
    private readonly IDocumentTokenizer _tokenizer;
    private readonly OpenAIAnswerOptions _options;

    public RagContextBuilder(
        IDocumentTokenizer tokenizer,
        IOptions<OpenAIAnswerOptions> options)
    {
        _tokenizer = tokenizer;
        _options = options.Value;
    }

    public RagContext Build(IReadOnlyList<RetrievedDocumentChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        var builder = new StringBuilder();
        builder.AppendLine(RagArchitecture.ContextStartDelimiter);
        var sources = new List<RagSource>(chunks.Count);
        var seenChunkIds = new HashSet<Guid>();
        var seenContents = new HashSet<string>(StringComparer.Ordinal);

        foreach (var chunk in chunks)
        {
            if (!seenChunkIds.Add(chunk.ChunkId) ||
                !seenContents.Add(chunk.Content))
            {
                continue;
            }

            var sourceId = $"S{sources.Count + 1}";
            var header = BuildHeader(sourceId, chunk);
            const string footer = "\n---\n";
            var closingTokens = _tokenizer.CountTokens(
                RagArchitecture.ContextEndDelimiter);
            var usedTokens = _tokenizer.CountTokens(builder.ToString());
            var framingTokens = _tokenizer.CountTokens(header + footer);
            var availableContentTokens =
                _options.MaxContextTokens - usedTokens - framingTokens - closingTokens;

            if (availableContentTokens <= 0)
            {
                break;
            }

            var content = chunk.Content;
            if (_tokenizer.CountTokens(content) > availableContentTokens)
            {
                var endIndex = _tokenizer.GetIndexByTokenCount(
                    content,
                    availableContentTokens);
                if (endIndex <= 0)
                {
                    break;
                }

                content = content[..endIndex].TrimEnd();
            }

            if (content.Length == 0)
            {
                continue;
            }

            var candidateText = builder.ToString() + header + content + footer +
                RagArchitecture.ContextEndDelimiter;
            var candidateTokenCount = _tokenizer.CountTokens(candidateText);
            while (candidateTokenCount > _options.MaxContextTokens)
            {
                var contentTokenCount = _tokenizer.CountTokens(content);
                var reducedTokenCount = contentTokenCount -
                    Math.Max(1, candidateTokenCount - _options.MaxContextTokens);
                if (reducedTokenCount <= 0)
                {
                    content = string.Empty;
                    break;
                }

                var endIndex = _tokenizer.GetIndexByTokenCount(
                    content,
                    reducedTokenCount);
                if (endIndex <= 0)
                {
                    content = string.Empty;
                    break;
                }

                content = content[..endIndex].TrimEnd();
                candidateText = builder.ToString() + header + content + footer +
                    RagArchitecture.ContextEndDelimiter;
                candidateTokenCount = _tokenizer.CountTokens(candidateText);
            }

            if (content.Length == 0)
            {
                break;
            }

            builder.Append(header);
            builder.Append(content);
            builder.Append(footer);
            sources.Add(new RagSource(sourceId, chunk));

            if (!string.Equals(content, chunk.Content, StringComparison.Ordinal))
            {
                break;
            }
        }

        builder.Append(RagArchitecture.ContextEndDelimiter);
        var text = builder.ToString();
        return new RagContext(text, _tokenizer.CountTokens(text), sources);
    }

    private static string BuildHeader(
        string sourceId,
        RetrievedDocumentChunk chunk)
    {
        var pages = (chunk.PageStart, chunk.PageEnd) switch
        {
            (null, null) => "Unavailable",
            (int start, null) => start.ToString(),
            (null, int end) => end.ToString(),
            (int start, int end) when start == end => start.ToString(),
            (int start, int end) => $"{start}-{end}"
        };
        var heading = string.IsNullOrWhiteSpace(chunk.Heading)
            ? "Unavailable"
            : chunk.Heading;

        return $"""
            [{sourceId}]
            Document: {chunk.DocumentName}
            Page: {pages}
            Chunk: {chunk.ChunkIndex + 1}
            Heading: {heading}
            Content:
            """ + "\n";
    }
}
