using System.ClientModel;
using System.Text.Json;
using AI.DocumentAssistant.Server.Rag;
using Microsoft.Extensions.Options;
using OpenAI.Responses;

namespace AI.DocumentAssistant.Server.Retrieval;

#pragma warning disable OPENAI001
public sealed class OpenAIRetrievalReranker : IRetrievalReranker
{
    private const string SafeFailureMessage =
        "Retrieval reranking could not be completed.";

    private static readonly BinaryData StructuredOutputSchema =
        BinaryData.FromString(RetrievalRerankingPrompt.JsonSchema);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAIRetrievalReranker> _logger;
    private readonly string _model;
    private readonly object _clientLock = new();
    private ResponsesClient? _client;

    public OpenAIRetrievalReranker(
        IConfiguration configuration,
        IOptions<OpenAIRerankingOptions> rerankingOptions,
        IOptions<OpenAIAnswerOptions> answerOptions,
        ILogger<OpenAIRetrievalReranker> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _model = ResolveModel(
            rerankingOptions.Value.RerankingModel,
            answerOptions.Value.AnswerModel);
    }

    public async Task<RetrievalRerankerResult> RerankAsync(
        RetrievalRerankingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var options = new CreateResponseOptions
        {
            Model = _model,
            Instructions = RetrievalRerankingPrompt.SystemInstructions,
            MaxOutputTokenCount = RetrievalRerankingLimits.MaximumOutputTokens,
            StoredOutputEnabled = false,
            ReasoningOptions = new ResponseReasoningOptions
            {
                ReasoningEffortLevel = ResponseReasoningEffortLevel.Low
            },
            TextOptions = new ResponseTextOptions
            {
                TextFormat = ResponseTextFormat.CreateJsonSchemaFormat(
                    "retrieval_reranking",
                    StructuredOutputSchema,
                    jsonSchemaIsStrict: true)
            }
        };
        options.InputItems.Add(ResponseItem.CreateUserMessageItem(
            RetrievalRerankingPrompt.BuildUserInput(
                request.Question,
                request.Candidates)));

        try
        {
            ResponseResult response = await GetClient().CreateResponseAsync(
                options,
                cancellationToken);
            var json = response.GetOutputText()?.Trim();
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new RetrievalRerankingException(SafeFailureMessage);
            }

            var result = JsonSerializer.Deserialize<RetrievalRerankerResult>(
                json,
                SerializerOptions);
            return result ?? throw new RetrievalRerankingException(
                SafeFailureMessage);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RetrievalRerankingException)
        {
            throw;
        }
        catch (Exception exception)
        {
            int? providerStatus = exception is ClientResultException clientResultException
                ? clientResultException.Status
                : null;
            _logger.LogWarning(
                "OpenAI retrieval-reranking request failed with exception type {ExceptionType} and HTTP status {ProviderStatus} using model {RerankingModel}. Question, candidate content, prompt, response payload, and provider details were omitted.",
                exception.GetType().FullName,
                providerStatus,
                _model);
            throw new RetrievalRerankingException(
                SafeFailureMessage,
                exception);
        }
    }

    public static string ResolveModel(
        string? configuredRerankingModel,
        string answerModel) =>
        string.IsNullOrWhiteSpace(configuredRerankingModel)
            ? answerModel
            : configuredRerankingModel.Trim();

    private ResponsesClient GetClient()
    {
        lock (_clientLock)
        {
            if (_client is not null)
            {
                return _client;
            }

            var apiKey = _configuration[$"{OpenAIRerankingOptions.SectionName}:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new RetrievalRerankingException(
                    "Retrieval reranking configuration is unavailable.");
            }

            _client = new ResponsesClient(apiKey);
            return _client;
        }
    }
}
#pragma warning restore OPENAI001
