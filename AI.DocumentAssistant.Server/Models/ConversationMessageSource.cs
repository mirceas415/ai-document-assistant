namespace AI.DocumentAssistant.Server.Models;

public sealed class ConversationMessageSource
{
    public Guid Id { get; set; }

    public Guid ConversationMessageId { get; set; }

    public ConversationMessage ConversationMessage { get; set; } = null!;

    public int SourceIndex { get; set; }

    public string SourceId { get; set; } = string.Empty;

    // Deliberately snapshot identifiers rather than foreign keys. Deleting a source
    // document must not remove or invalidate historical conversation citations.
    public Guid? DocumentId { get; set; }

    public string DocumentName { get; set; } = string.Empty;

    public Guid? DocumentChunkId { get; set; }

    public int ChunkIndex { get; set; }

    public int? PageStart { get; set; }

    public int? PageEnd { get; set; }

    public string? Heading { get; set; }

    public string Excerpt { get; set; } = string.Empty;
}
