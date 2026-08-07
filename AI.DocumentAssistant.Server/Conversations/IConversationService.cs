using AI.DocumentAssistant.Server.Contracts;

namespace AI.DocumentAssistant.Server.Conversations;

public interface IConversationService
{
    Task<IReadOnlyList<ConversationSummaryResponse>?> ListAsync(
        Guid ownerId,
        Guid projectId,
        CancellationToken cancellationToken);

    Task<ConversationResponse?> CreateAsync(
        Guid ownerId,
        Guid projectId,
        CancellationToken cancellationToken);

    Task<ConversationResponse?> GetAsync(
        Guid ownerId,
        Guid projectId,
        Guid conversationId,
        CancellationToken cancellationToken);

    Task<ConversationResponse?> RenameAsync(
        Guid ownerId,
        Guid projectId,
        Guid conversationId,
        string title,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        Guid ownerId,
        Guid projectId,
        Guid conversationId,
        CancellationToken cancellationToken);

    Task<ConversationMessageResponse?> AskAsync(
        Guid ownerId,
        Guid projectId,
        Guid conversationId,
        string question,
        CancellationToken cancellationToken);

    Task<ConversationMessageResponse?> AskAsync(
        Guid ownerId,
        Guid projectId,
        Guid conversationId,
        string question,
        Guid? retryMessageId,
        CancellationToken cancellationToken) =>
        AskAsync(ownerId, projectId, conversationId, question, cancellationToken);
}
