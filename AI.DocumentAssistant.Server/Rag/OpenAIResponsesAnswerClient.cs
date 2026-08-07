using OpenAI.Responses;
using System.ClientModel;

namespace AI.DocumentAssistant.Server.Rag;

#pragma warning disable OPENAI001
public sealed class OpenAIResponsesAnswerClient : IOpenAIAnswerClient
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAIResponsesAnswerClient> _logger;
    private readonly object _clientLock = new();
    private ResponsesClient? _client;

    public OpenAIResponsesAnswerClient(
        IConfiguration configuration,
        ILogger<OpenAIResponsesAnswerClient> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> GenerateAnswerAsync(
        string model,
        string instructions,
        string userInput,
        int maximumOutputTokens,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = new CreateResponseOptions
        {
            Model = model,
            Instructions = instructions,
            MaxOutputTokenCount = maximumOutputTokens,
            StoredOutputEnabled = false,
            ReasoningOptions = new ResponseReasoningOptions
            {
                ReasoningEffortLevel = ResponseReasoningEffortLevel.Low
            }
        };
        options.InputItems.Add(ResponseItem.CreateUserMessageItem(userInput));

        try
        {
            ResponseResult response = await GetClient().CreateResponseAsync(
                options,
                cancellationToken);
            var answer = response.GetOutputText()?.Trim();
            if (string.IsNullOrWhiteSpace(answer))
            {
                throw new GroundedAnswerException(
                    "An answer could not be generated. Please try again.");
            }

            return answer;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GroundedAnswerException)
        {
            throw;
        }
        catch (Exception exception)
        {
            int? providerStatus = exception is ClientResultException clientResultException
                ? clientResultException.Status
                : null;
            _logger.LogError(
                "OpenAI Responses answer request failed with exception type {ExceptionType} and HTTP status {ProviderStatus} using model {AnswerModel}. Prompt, question, context, answer, and provider response details were omitted.",
                exception.GetType().FullName,
                providerStatus,
                model);
            throw new GroundedAnswerException(
                "An answer could not be generated. Please try again.",
                exception);
        }
    }

    private ResponsesClient GetClient()
    {
        lock (_clientLock)
        {
            if (_client is not null)
            {
                return _client;
            }

            var apiKey = _configuration[$"{OpenAIAnswerOptions.SectionName}:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new GroundedAnswerException(
                    "Answer service configuration is unavailable.");
            }

            _client = new ResponsesClient(apiKey);
            return _client;
        }
    }
}
#pragma warning restore OPENAI001
