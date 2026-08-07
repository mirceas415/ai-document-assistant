namespace AI.DocumentAssistant.Server.Rag;

public interface IProjectQuestionAnsweringService
{
    Task<ProjectAnswerResult?> AnswerAsync(
        Guid ownerId,
        Guid projectId,
        string question,
        CancellationToken cancellationToken);

    Task<ProjectAnswerResult?> AnswerAsync(
        Guid ownerId,
        Guid projectId,
        string question,
        IReadOnlyList<ConversationHistoryMessage> history,
        CancellationToken cancellationToken) =>
        AnswerAsync(ownerId, projectId, question, cancellationToken);
}

public sealed record ProjectAnswerResult(
    string Answer,
    IReadOnlyList<ProjectAnswerSource> Sources);

public sealed record ProjectAnswerSource(
    string SourceId,
    Guid DocumentId,
    string DocumentName,
    Guid ChunkId,
    int ChunkIndex,
    int? PageStart,
    int? PageEnd,
    string? Heading,
    string Excerpt);
