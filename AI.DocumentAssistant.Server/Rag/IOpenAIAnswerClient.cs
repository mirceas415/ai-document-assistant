namespace AI.DocumentAssistant.Server.Rag;

public interface IOpenAIAnswerClient
{
    Task<string> GenerateAnswerAsync(
        string model,
        string instructions,
        string userInput,
        int maximumOutputTokens,
        CancellationToken cancellationToken);
}
