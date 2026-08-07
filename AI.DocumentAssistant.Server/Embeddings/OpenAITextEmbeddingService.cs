using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Embeddings;

public sealed class OpenAITextEmbeddingService : ITextEmbeddingService
{
    private readonly IOpenAIEmbeddingClient _client;
    private readonly OpenAIEmbeddingOptions _options;
    private readonly ILogger<OpenAITextEmbeddingService> _logger;

    public OpenAITextEmbeddingService(
        IOpenAIEmbeddingClient client,
        IOptions<OpenAIEmbeddingOptions> options,
        ILogger<OpenAITextEmbeddingService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TextEmbeddingResult> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (texts.Count == 0)
        {
            throw new DocumentEmbeddingException(
                "Document chunks are required before embeddings can be generated.");
        }

        for (var index = 0; index < texts.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(texts[index]))
            {
                throw new DocumentEmbeddingException(
                    "Empty document chunks cannot be embedded.");
            }
        }

        var stopwatch = Stopwatch.StartNew();
        var batchCount = (texts.Count + _options.BatchSize - 1) / _options.BatchSize;
        var embeddings = new List<float[]>(texts.Count);

        _logger.LogInformation(
            "Generating embeddings for {InputCount} inputs in {BatchCount} sequential batches of at most {BatchSize} using model {EmbeddingModel} with {EmbeddingDimensions} dimensions.",
            texts.Count,
            batchCount,
            _options.BatchSize,
            _options.EmbeddingModel,
            _options.EmbeddingDimensions);

        for (var offset = 0; offset < texts.Count; offset += _options.BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(_options.BatchSize, texts.Count - offset);
            var batch = new string[count];
            for (var index = 0; index < count; index++)
            {
                batch[index] = texts[offset + index];
            }

            var batchEmbeddings = await _client.GenerateEmbeddingsAsync(
                batch,
                _options.EmbeddingModel,
                _options.EmbeddingDimensions,
                cancellationToken);

            if (batchEmbeddings.Count != count)
            {
                throw new DocumentEmbeddingException(
                    "The embedding service returned an unexpected result count. Please retry.");
            }

            foreach (var embedding in batchEmbeddings)
            {
                if (embedding is null || embedding.Length != _options.EmbeddingDimensions)
                {
                    throw new DocumentEmbeddingException(
                        "The embedding service returned an unexpected vector size. Please retry.");
                }

                if (embedding.Any(value => !float.IsFinite(value)))
                {
                    throw new DocumentEmbeddingException(
                        "The embedding service returned invalid vector values. Please retry.");
                }

                embeddings.Add(embedding);
            }
        }

        var result = new TextEmbeddingResult(
            _options.EmbeddingModel,
            _options.EmbeddingDimensions,
            embeddings);
        EmbeddingResultValidator.Validate(
            result,
            texts.Count,
            _options.EmbeddingModel,
            _options.EmbeddingDimensions);

        stopwatch.Stop();
        _logger.LogInformation(
            "Generated {EmbeddingCount} embeddings in {DurationMs} ms using model {EmbeddingModel} with {EmbeddingDimensions} dimensions across {BatchCount} batches.",
            embeddings.Count,
            stopwatch.ElapsedMilliseconds,
            _options.EmbeddingModel,
            _options.EmbeddingDimensions,
            batchCount);

        return result;
    }
}
