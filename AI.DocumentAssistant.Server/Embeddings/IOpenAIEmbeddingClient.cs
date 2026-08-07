namespace AI.DocumentAssistant.Server.Embeddings;

/// <summary>
/// Narrow provider-specific seam used by the OpenAI adapter's offline contract tests.
/// Application pipeline code depends only on <see cref="ITextEmbeddingService"/>.
/// </summary>
public interface IOpenAIEmbeddingClient
{
    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        string model,
        int dimensions,
        CancellationToken cancellationToken);
}
