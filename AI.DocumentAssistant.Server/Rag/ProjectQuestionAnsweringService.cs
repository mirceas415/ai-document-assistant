using System.Text.RegularExpressions;
using AI.DocumentAssistant.Server.Retrieval;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Rag;

public sealed partial class ProjectQuestionAnsweringService
    : IProjectQuestionAnsweringService
{
    private const string EnglishNoEvidenceAnswer =
        "I couldn't find enough information in the documents in this project to answer that question.";

    private const string RomanianNoEvidenceAnswer =
        "Nu am găsit suficiente informații în documentele acestui proiect pentru a răspunde la întrebare.";

    private readonly ISemanticRetrievalService _retrievalService;
    private readonly IRagContextBuilder _contextBuilder;
    private readonly IGroundedAnswerService _answerService;
    private readonly OpenAIAnswerOptions _options;
    private readonly ILogger<ProjectQuestionAnsweringService> _logger;

    public ProjectQuestionAnsweringService(
        ISemanticRetrievalService retrievalService,
        IRagContextBuilder contextBuilder,
        IGroundedAnswerService answerService,
        IOptions<OpenAIAnswerOptions> options,
        ILogger<ProjectQuestionAnsweringService> logger)
    {
        _retrievalService = retrievalService;
        _contextBuilder = contextBuilder;
        _answerService = answerService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ProjectAnswerResult?> AnswerAsync(
        Guid ownerId,
        Guid projectId,
        string question,
        CancellationToken cancellationToken)
        => await AnswerAsync(
            ownerId,
            projectId,
            question,
            [],
            cancellationToken);

    public async Task<ProjectAnswerResult?> AnswerAsync(
        Guid ownerId,
        Guid projectId,
        string question,
        IReadOnlyList<ConversationHistoryMessage> history,
        CancellationToken cancellationToken)
    {
        var normalizedQuestion = question.Trim();
        var retrieval = await _retrievalService.SearchAsync(
            ownerId,
            projectId,
            normalizedQuestion,
            _options.AnswerRetrievalTopK,
            cancellationToken);
        if (retrieval is null)
        {
            return null;
        }

        if (retrieval.Chunks.Count == 0)
        {
            _logger.LogInformation(
                "Answer generation skipped for project {ProjectId} because semantic retrieval returned zero eligible chunks.",
                projectId);
            return new ProjectAnswerResult(
                GetNoEvidenceAnswer(normalizedQuestion),
                []);
        }

        var context = _contextBuilder.Build(retrieval.Chunks);
        if (context.Sources.Count == 0)
        {
            _logger.LogInformation(
                "Answer generation skipped for project {ProjectId} because no retrieved chunks fit the configured context budget.",
                projectId);
            return new ProjectAnswerResult(
                GetNoEvidenceAnswer(normalizedQuestion),
                []);
        }

        var modelAnswer = await _answerService.GenerateAnswerAsync(
            normalizedQuestion,
            context,
            history,
            cancellationToken);
        var sourcesById = context.Sources.ToDictionary(
            source => source.SourceId,
            StringComparer.Ordinal);
        var validSourceIds = modelAnswer.ReferencedSourceIds
            .Select(sourceId => sourceId.ToUpperInvariant())
            .Where(sourcesById.ContainsKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var answer = RemoveUnknownCitations(modelAnswer.Answer, sourcesById.Keys);
        if (string.IsNullOrWhiteSpace(answer))
        {
            answer = GetNoEvidenceAnswer(normalizedQuestion);
        }

        var sources = validSourceIds
            .Select(sourceId => ToAnswerSource(sourcesById[sourceId]))
            .ToArray();

        _logger.LogInformation(
            "Project answer completed for project {ProjectId} from {RetrievedChunkCount} retrieved chunks, {ContextSourceCount} bounded context sources, and {CitedSourceCount} validated citations.",
            projectId,
            retrieval.Chunks.Count,
            context.Sources.Count,
            sources.Length);

        return new ProjectAnswerResult(answer, sources);
    }

    private ProjectAnswerSource ToAnswerSource(RagSource source)
    {
        var chunk = source.Chunk;
        return new ProjectAnswerSource(
            source.SourceId,
            chunk.DocumentId,
            chunk.DocumentName,
            chunk.ChunkId,
            chunk.ChunkIndex,
            chunk.PageStart,
            chunk.PageEnd,
            chunk.Heading,
            CreateExcerpt(chunk.Content, _options.SourceExcerptCharacters));
    }

    private static string CreateExcerpt(string content, int maximumCharacters)
    {
        if (content.Length <= maximumCharacters)
        {
            return content;
        }

        var length = maximumCharacters;
        if (length > 0 && char.IsHighSurrogate(content[length - 1]))
        {
            length--;
        }

        return content[..length].TrimEnd() + "…";
    }

    private static string RemoveUnknownCitations(
        string answer,
        IEnumerable<string> allowedSourceIds)
    {
        var allowed = allowedSourceIds.ToHashSet(StringComparer.Ordinal);
        return AnySourceCitationPattern().Replace(
            answer,
            match =>
            {
                var sourceId = match.Groups[1].Value.ToUpperInvariant();
                return allowed.Contains(sourceId) ? $"[{sourceId}]" : string.Empty;
            }).Trim();
    }

    private static string GetNoEvidenceAnswer(string question) =>
        LooksRomanian(question)
            ? RomanianNoEvidenceAnswer
            : EnglishNoEvidenceAnswer;

    private static bool LooksRomanian(string question)
    {
        if (question.IndexOfAny(['ă', 'â', 'î', 'ș', 'ş', 'ț', 'ţ']) >= 0)
        {
            return true;
        }

        return RomanianQuestionPattern().IsMatch(question);
    }

    [GeneratedRegex(@"\[(S[1-9][0-9]*)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnySourceCitationPattern();

    [GeneratedRegex(
        @"\b(care|ce|cum|când|cand|unde|cine|de ce|este|sunt|documentele|informațiile|informatiile)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RomanianQuestionPattern();
}
