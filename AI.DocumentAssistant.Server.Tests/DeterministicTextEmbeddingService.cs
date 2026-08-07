using AI.DocumentAssistant.Server.Embeddings;

namespace AI.DocumentAssistant.Server.Tests;

internal sealed class DeterministicTextEmbeddingService : ITextEmbeddingService
{
    private int _nextVectorOrdinal;

    public List<IReadOnlyList<string>> Calls { get; } = [];

    public int RemainingFailures { get; set; }

    public string Model { get; init; } = EmbeddingArchitecture.DefaultModel;

    public int Dimensions { get; init; } = EmbeddingArchitecture.Dimensions;

    public Func<CancellationToken, Task>? BeforeGenerateAsync { get; init; }

    public string FailureMessage { get; init; } =
        "Document embeddings could not be generated. Please try again.";

    public async Task<TextEmbeddingResult> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var recordedTexts = texts.ToArray();
        Calls.Add(recordedTexts);

        if (BeforeGenerateAsync is not null)
        {
            await BeforeGenerateAsync(cancellationToken);
        }

        if (RemainingFailures > 0)
        {
            RemainingFailures--;
            throw new DocumentEmbeddingException(FailureMessage);
        }

        var vectors = recordedTexts
            .Select(_ => CreateVector(_nextVectorOrdinal++))
            .ToArray();

        return new TextEmbeddingResult(Model, Dimensions, vectors);
    }

    private float[] CreateVector(int ordinal)
    {
        var vector = new float[Dimensions];
        vector[0] = ordinal + 1;
        vector[^1] = -(ordinal + 1);
        return vector;
    }
}
