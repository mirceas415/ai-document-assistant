namespace AI.DocumentAssistant.Server.Rag;

public interface IGroundedAnswerService
{
    Task<GroundedModelAnswer> GenerateAnswerAsync(
        string question,
        RagContext context,
        CancellationToken cancellationToken);
}

public sealed record GroundedModelAnswer(
    string Answer,
    IReadOnlyList<string> ReferencedSourceIds);
