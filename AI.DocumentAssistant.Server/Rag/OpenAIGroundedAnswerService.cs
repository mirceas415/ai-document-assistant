using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Rag;

public sealed partial class OpenAIGroundedAnswerService : IGroundedAnswerService
{
    private readonly IOpenAIAnswerClient _client;
    private readonly OpenAIAnswerOptions _options;
    private readonly ILogger<OpenAIGroundedAnswerService> _logger;

    public OpenAIGroundedAnswerService(
        IOpenAIAnswerClient client,
        IOptions<OpenAIAnswerOptions> options,
        ILogger<OpenAIGroundedAnswerService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GroundedModelAnswer> GenerateAnswerAsync(
        string question,
        RagContext context,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(context);

        var userInput = $"""
            Answer this user question using only the delimited untrusted document context below.

            USER QUESTION:
            {question}

            {context.Text}
            """;
        var answer = await _client.GenerateAnswerAsync(
            _options.AnswerModel,
            RagArchitecture.GroundingInstructions,
            userInput,
            _options.MaxAnswerTokens,
            cancellationToken);
        var referencedSourceIds = SourceCitationPattern()
            .Matches(answer)
            .Select(match => match.Groups[1].Value.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        _logger.LogInformation(
            "Grounded answer generation completed using model {AnswerModel} with {ContextTokenCount} approximate context tokens, {SourceCount} supplied sources, and {ReferencedSourceCount} referenced source identifiers.",
            _options.AnswerModel,
            context.ApproximateTokenCount,
            context.Sources.Count,
            referencedSourceIds.Length);

        return new GroundedModelAnswer(answer, referencedSourceIds);
    }

    [GeneratedRegex(@"\[(S[1-9][0-9]*)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SourceCitationPattern();
}
