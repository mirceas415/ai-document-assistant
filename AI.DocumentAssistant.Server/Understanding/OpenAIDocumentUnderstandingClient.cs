using System.ClientModel;
using System.Text.Json;
using OpenAI.Responses;

namespace AI.DocumentAssistant.Server.Understanding;

#pragma warning disable OPENAI001
public sealed class OpenAIDocumentUnderstandingClient : IDocumentUnderstandingClient
{
    private static readonly BinaryData StructuredOutputSchema =
        BinaryData.FromString(DocumentUnderstandingPrompt.JsonSchema);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAIDocumentUnderstandingClient> _logger;
    private readonly object _clientLock = new();
    private ResponsesClient? _client;

    public OpenAIDocumentUnderstandingClient(
        IConfiguration configuration,
        ILogger<OpenAIDocumentUnderstandingClient> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<DocumentUnderstandingProviderResult> AnalyzeAsync(
        string model,
        string documentContent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = new CreateResponseOptions
        {
            Model = model,
            Instructions = DocumentUnderstandingPrompt.SystemInstructions,
            MaxOutputTokenCount = DocumentUnderstandingLimits.MaximumOutputTokens,
            StoredOutputEnabled = false,
            ReasoningOptions = new ResponseReasoningOptions
            {
                ReasoningEffortLevel = ResponseReasoningEffortLevel.Low
            },
            TextOptions = new ResponseTextOptions
            {
                TextFormat = ResponseTextFormat.CreateJsonSchemaFormat(
                    "document_understanding",
                    StructuredOutputSchema,
                    jsonSchemaIsStrict: true)
            }
        };
        options.InputItems.Add(ResponseItem.CreateUserMessageItem(
            DocumentUnderstandingPrompt.BuildUserInput(documentContent)));

        try
        {
            ResponseResult response = await GetClient().CreateResponseAsync(
                options,
                cancellationToken);
            var json = response.GetOutputText()?.Trim();
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new DocumentUnderstandingException(
                    DocumentUnderstandingArchitecture.SafeFailureMessage);
            }

            var result = JsonSerializer.Deserialize<DocumentUnderstandingProviderResult>(
                json,
                SerializerOptions);
            return result ?? throw new DocumentUnderstandingException(
                DocumentUnderstandingArchitecture.SafeFailureMessage);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DocumentUnderstandingException)
        {
            throw;
        }
        catch (Exception exception)
        {
            int? providerStatus = exception is ClientResultException clientResultException
                ? clientResultException.Status
                : null;
            _logger.LogError(
                "OpenAI document-understanding request failed with exception type {ExceptionType} and HTTP status {ProviderStatus} using model {UnderstandingModel}. Document content, prompt, response payload, and provider details were omitted.",
                exception.GetType().FullName,
                providerStatus,
                model);
            throw new DocumentUnderstandingException(
                DocumentUnderstandingArchitecture.SafeFailureMessage,
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

            var apiKey = _configuration[
                $"{OpenAIDocumentUnderstandingOptions.SectionName}:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new DocumentUnderstandingException(
                    "Document understanding service configuration is unavailable.");
            }

            _client = new ResponsesClient(apiKey);
            return _client;
        }
    }
}
#pragma warning restore OPENAI001
