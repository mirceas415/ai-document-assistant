namespace AI.DocumentAssistant.Server.Embeddings;

public interface ITextEmbeddingService
{
    Task<TextEmbeddingResult> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken);
}

public sealed record TextEmbeddingResult(
    string Model,
    int Dimensions,
    IReadOnlyList<float[]> Embeddings);
