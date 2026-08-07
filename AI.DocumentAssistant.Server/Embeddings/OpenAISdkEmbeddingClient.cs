using OpenAI.Embeddings;
using System.ClientModel;

namespace AI.DocumentAssistant.Server.Embeddings;

public sealed class OpenAISdkEmbeddingClient : IOpenAIEmbeddingClient
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAISdkEmbeddingClient> _logger;
    private readonly object _clientLock = new();
    private EmbeddingClient? _client;
    private string? _clientModel;

    public OpenAISdkEmbeddingClient(
        IConfiguration configuration,
        ILogger<OpenAISdkEmbeddingClient> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        string model,
        int dimensions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var generationOptions = new EmbeddingGenerationOptions
        {
            Dimensions = dimensions
        };

        try
        {
            var client = GetClient(model);
            OpenAIEmbeddingCollection response = await client.GenerateEmbeddingsAsync(
                inputs,
                generationOptions,
                cancellationToken);

            if (response.Count != inputs.Count)
            {
                throw new DocumentEmbeddingException(
                    "The embedding service returned an unexpected result count. Please retry.");
            }

            var ordered = new float[inputs.Count][];
            foreach (var embedding in response)
            {
                if (embedding.Index < 0 ||
                    embedding.Index >= ordered.Length ||
                    ordered[embedding.Index] is not null)
                {
                    throw new DocumentEmbeddingException(
                        "The embedding service returned an invalid result order. Please retry.");
                }

                ordered[embedding.Index] = embedding.ToFloats().ToArray();
            }

            if (ordered.Any(vector => vector is null))
            {
                throw new DocumentEmbeddingException(
                    "The embedding service returned an invalid result order. Please retry.");
            }

            return ordered;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DocumentEmbeddingException)
        {
            throw;
        }
        catch (Exception exception)
        {
            int? providerStatus = exception is ClientResultException clientResultException
                ? clientResultException.Status
                : null;
            _logger.LogError(
                "OpenAI embedding request failed with exception type {ExceptionType} and HTTP status {ProviderStatus} for {InputCount} inputs using model {EmbeddingModel} with {EmbeddingDimensions} dimensions. Provider response details were omitted.",
                exception.GetType().FullName,
                providerStatus,
                inputs.Count,
                model,
                dimensions);

            throw new DocumentEmbeddingException(
                "Document embeddings could not be generated. Please try again.");
        }
    }

    private EmbeddingClient GetClient(string model)
    {
        lock (_clientLock)
        {
            if (_client is not null && string.Equals(_clientModel, model, StringComparison.Ordinal))
            {
                return _client;
            }

            var apiKey = _configuration[$"{OpenAIEmbeddingOptions.SectionName}:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new DocumentEmbeddingException(
                    "Embedding service configuration is unavailable.");
            }

            _client = new EmbeddingClient(model, apiKey);
            _clientModel = model;
            return _client;
        }
    }
}
