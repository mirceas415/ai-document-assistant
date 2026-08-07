namespace AI.DocumentAssistant.Server.Models;

public sealed class ConversationMessage
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public Conversation Conversation { get; set; } = null!;

    public ConversationMessageRole Role { get; set; }

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public int Sequence { get; set; }

    public ICollection<ConversationMessageSource> Sources { get; } =
        new List<ConversationMessageSource>();
}

public enum ConversationMessageRole
{
    User,
    Assistant
}
