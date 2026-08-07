using System.Text;
using AI.DocumentAssistant.Server.Chunking;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Rag;

public interface IConversationHistoryContextBuilder
{
    ConversationHistoryContext Build(
        IReadOnlyList<ConversationHistoryMessage> messages);
}

public sealed class ConversationHistoryContextBuilder
    : IConversationHistoryContextBuilder
{
    private readonly IDocumentTokenizer _tokenizer;
    private readonly OpenAIAnswerOptions _options;

    public ConversationHistoryContextBuilder(
        IDocumentTokenizer tokenizer,
        IOptions<OpenAIAnswerOptions> options)
    {
        _tokenizer = tokenizer;
        _options = options.Value;
    }

    public ConversationHistoryContext Build(
        IReadOnlyList<ConversationHistoryMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (_options.RecentConversationMessageCount == 0 ||
            _options.MaxConversationContextTokens == 0 ||
            messages.Count == 0)
        {
            return new ConversationHistoryContext(string.Empty, 0, 0);
        }

        var recent = messages
            .TakeLast(_options.RecentConversationMessageCount)
            .ToArray();
        var selected = new List<(ConversationHistoryMessage Message, string Content)>();
        var framingTokens = _tokenizer.CountTokens(
            RagArchitecture.ConversationContextStartDelimiter + "\n" +
            RagArchitecture.ConversationContextEndDelimiter);
        var remainingTokens = _options.MaxConversationContextTokens - framingTokens;

        // Work backwards so the newest turns are retained when the token budget is
        // tighter than the configured message-count bound.
        foreach (var message in recent.Reverse())
        {
            var role = message.Role == ConversationHistoryRole.User
                ? "USER"
                : "ASSISTANT";
            var prefix = $"{role}:\n";
            const string suffix = "\n---\n";
            var contentTokens = remainingTokens -
                _tokenizer.CountTokens(prefix + suffix);
            if (contentTokens <= 0)
            {
                break;
            }

            var content = message.Content;
            if (_tokenizer.CountTokens(content) > contentTokens)
            {
                var end = _tokenizer.GetIndexByTokenCount(content, contentTokens);
                content = end > 0 ? content[..end].TrimEnd() : string.Empty;
            }

            if (content.Length == 0)
            {
                break;
            }

            var blockTokens = _tokenizer.CountTokens(prefix + content + suffix);
            selected.Add((message, content));
            remainingTokens -= blockTokens;
            if (content.Length < message.Content.Length)
            {
                break;
            }
        }

        selected.Reverse();
        var builder = new StringBuilder();
        builder.AppendLine(RagArchitecture.ConversationContextStartDelimiter);
        foreach (var (message, content) in selected)
        {
            builder.Append(message.Role == ConversationHistoryRole.User ? "USER:\n" : "ASSISTANT:\n")
                .Append(content)
                .Append("\n---\n");
        }
        builder.Append(RagArchitecture.ConversationContextEndDelimiter);
        var text = builder.ToString();
        while (_tokenizer.CountTokens(text) > _options.MaxConversationContextTokens &&
               selected.Count > 0)
        {
            if (selected.Count == 1)
            {
                var only = selected[0];
                var currentTokens = _tokenizer.CountTokens(only.Content);
                var excess = _tokenizer.CountTokens(text) -
                    _options.MaxConversationContextTokens;
                var reducedTokens = currentTokens - Math.Max(1, excess);
                var end = reducedTokens > 0
                    ? _tokenizer.GetIndexByTokenCount(only.Content, reducedTokens)
                    : 0;
                if (end <= 0)
                {
                    selected.Clear();
                }
                else
                {
                    selected[0] = (only.Message, only.Content[..end].TrimEnd());
                }
            }
            else
            {
                selected.RemoveAt(0);
            }

            builder.Clear().AppendLine(RagArchitecture.ConversationContextStartDelimiter);
            foreach (var (message, content) in selected)
            {
                builder.Append(message.Role == ConversationHistoryRole.User ? "USER:\n" : "ASSISTANT:\n")
                    .Append(content)
                    .Append("\n---\n");
            }
            builder.Append(RagArchitecture.ConversationContextEndDelimiter);
            text = builder.ToString();
        }

        return new ConversationHistoryContext(
            text,
            _tokenizer.CountTokens(text),
            selected.Count);
    }
}

public sealed record ConversationHistoryContext(
    string Text,
    int ApproximateTokenCount,
    int IncludedMessageCount);

public sealed record ConversationHistoryMessage(
    ConversationHistoryRole Role,
    string Content);

public enum ConversationHistoryRole
{
    User,
    Assistant
}
