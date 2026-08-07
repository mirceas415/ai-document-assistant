namespace AI.DocumentAssistant.Server.Models;

public sealed class Conversation
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Project Project { get; set; } = null!;

    public string Title { get; set; } = ConversationLimits.DefaultTitle;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<ConversationMessage> Messages { get; } =
        new List<ConversationMessage>();
}

public static class ConversationLimits
{
    public const string DefaultTitle = "New chat";

    public const int MaximumTitleLength = 80;

    public const int GeneratedTitleLength = 72;

    public const int MaximumSourceIdLength = 16;

    public const int MaximumDocumentNameLength = 255;

    public const int MaximumHeadingLength = 500;

    public const int MaximumSourceExcerptLength = 500;
}
