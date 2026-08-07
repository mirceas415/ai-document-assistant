namespace AI.DocumentAssistant.Server.Rag;

public interface IGroundedAnswerService
{
    Task<GroundedModelAnswer> GenerateAnswerAsync(
        string question,
        RagContext context,
        CancellationToken cancellationToken);

    Task<GroundedModelAnswer> GenerateAnswerAsync(
        string question,
        RagContext context,
        IReadOnlyList<ConversationHistoryMessage> history,
        CancellationToken cancellationToken) =>
        GenerateAnswerAsync(question, context, cancellationToken);
}

public sealed record GroundedModelAnswer(
    string Answer,
    IReadOnlyList<string> ReferencedSourceIds);
