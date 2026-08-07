using System.Text.RegularExpressions;
using AI.DocumentAssistant.Server.Contracts;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Rag;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Conversations;

public sealed partial class ConversationService : IConversationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IProjectQuestionAnsweringService _questionAnsweringService;
    private readonly OpenAIAnswerOptions _answerOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ConversationService> _logger;

    public ConversationService(
        ApplicationDbContext dbContext,
        IProjectQuestionAnsweringService questionAnsweringService,
        IOptions<OpenAIAnswerOptions> answerOptions,
        TimeProvider timeProvider,
        ILogger<ConversationService> logger)
    {
        _dbContext = dbContext;
        _questionAnsweringService = questionAnsweringService;
        _answerOptions = answerOptions.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ConversationSummaryResponse>?> ListAsync(
        Guid ownerId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var projectExists = await _dbContext.Projects
            .AsNoTracking()
            .AnyAsync(
                project => project.Id == projectId && project.OwnerId == ownerId,
                cancellationToken);
        if (!projectExists)
        {
            return null;
        }

        return await _dbContext.Conversations
            .AsNoTracking()
            .Where(conversation =>
                conversation.ProjectId == projectId &&
                conversation.Project.OwnerId == ownerId)
            .OrderByDescending(conversation => conversation.UpdatedAtUtc)
            .ThenByDescending(conversation => conversation.CreatedAtUtc)
            .Select(conversation => new ConversationSummaryResponse(
                conversation.Id,
                conversation.Title,
                conversation.CreatedAtUtc,
                conversation.UpdatedAtUtc,
                conversation.Messages.Count,
                conversation.Messages.Sum(message => message.Sources.Count)))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<ConversationResponse?> CreateAsync(
        Guid ownerId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var projectExists = await _dbContext.Projects
            .AsNoTracking()
            .AnyAsync(
                project => project.Id == projectId && project.OwnerId == ownerId,
                cancellationToken);
        if (!projectExists)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Title = ConversationLimits.DefaultTitle,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        _dbContext.Conversations.Add(conversation);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(conversation, []);
    }

    public async Task<ConversationResponse?> GetAsync(
        Guid ownerId,
        Guid projectId,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var conversation = await OwnedConversationQuery(ownerId, projectId)
            .AsNoTracking()
            .Include(value => value.Messages.OrderBy(message => message.Sequence))
                .ThenInclude(message => message.Sources.OrderBy(source => source.SourceIndex))
            .SingleOrDefaultAsync(
                value => value.Id == conversationId,
                cancellationToken);
        return conversation is null
            ? null
            : ToResponse(conversation, conversation.Messages);
    }

    public async Task<ConversationResponse?> RenameAsync(
        Guid ownerId,
        Guid projectId,
        Guid conversationId,
        string title,
        CancellationToken cancellationToken)
    {
        var conversation = await OwnedConversationQuery(ownerId, projectId)
            .SingleOrDefaultAsync(
                value => value.Id == conversationId,
                cancellationToken);
        if (conversation is null)
        {
            return null;
        }

        conversation.Title = title;
        conversation.UpdatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(
            ownerId,
            projectId,
            conversationId,
            cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        Guid ownerId,
        Guid projectId,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var conversation = await OwnedConversationQuery(ownerId, projectId)
            .Include(value => value.Messages)
                .ThenInclude(message => message.Sources)
            .SingleOrDefaultAsync(
                value => value.Id == conversationId,
                cancellationToken);
        if (conversation is null)
        {
            return false;
        }

        _dbContext.Conversations.Remove(conversation);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ConversationMessageResponse?> AskAsync(
        Guid ownerId,
        Guid projectId,
        Guid conversationId,
        string question,
        CancellationToken cancellationToken)
        => await AskAsync(
            ownerId,
            projectId,
            conversationId,
            question,
            null,
            cancellationToken);

    public async Task<ConversationMessageResponse?> AskAsync(
        Guid ownerId,
        Guid projectId,
        Guid conversationId,
        string question,
        Guid? retryMessageId,
        CancellationToken cancellationToken)
    {
        var conversation = await OwnedConversationQuery(ownerId, projectId)
            .Include(value => value.Messages)
            .SingleOrDefaultAsync(
                value => value.Id == conversationId,
                cancellationToken);
        if (conversation is null)
        {
            return null;
        }

        var orderedMessages = conversation.Messages
            .OrderBy(message => message.Sequence)
            .ToArray();
        ConversationMessage? retryMessage = null;
        if (retryMessageId.HasValue)
        {
            retryMessage = orderedMessages.LastOrDefault();
            if (retryMessage is null ||
                retryMessage.Id != retryMessageId.Value ||
                retryMessage.Role != ConversationMessageRole.User ||
                !string.Equals(retryMessage.Content, question, StringComparison.Ordinal))
            {
                return null;
            }
        }

        var precedingMessages = orderedMessages
            .Where(message => retryMessage is null ||
                message.Sequence < retryMessage.Sequence)
            .TakeLast(_answerOptions.RecentConversationMessageCount)
            .Select(message => new ConversationHistoryMessage(
                message.Role == ConversationMessageRole.User
                    ? ConversationHistoryRole.User
                    : ConversationHistoryRole.Assistant,
                message.Content))
            .ToArray();
        var nextSequence = orderedMessages.Length == 0
            ? 1
            : orderedMessages[^1].Sequence + 1;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (retryMessage is null)
        {
            var userMessage = new ConversationMessage
            {
                Id = Guid.NewGuid(),
                ConversationId = conversation.Id,
                Role = ConversationMessageRole.User,
                Content = question,
                CreatedAtUtc = now,
                Sequence = nextSequence
            };
            conversation.Messages.Add(userMessage);
            _dbContext.ConversationMessages.Add(userMessage);
            if (!orderedMessages.Any(message =>
                    message.Role == ConversationMessageRole.User) &&
                conversation.Title == ConversationLimits.DefaultTitle)
            {
                conversation.Title = GenerateTitle(question);
            }

            conversation.UpdatedAtUtc = now;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // The provider call intentionally occurs after the user message commits and
        // without a database transaction spanning network I/O. A failure therefore
        // leaves a retryable user message but never a fabricated assistant message.
        var answer = await _questionAnsweringService.AnswerAsync(
            ownerId,
            projectId,
            question,
            precedingMessages,
            cancellationToken);
        if (answer is null)
        {
            return null;
        }

        var assistantNow = _timeProvider.GetUtcNow().UtcDateTime;
        var assistantMessage = new ConversationMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Role = ConversationMessageRole.Assistant,
            Content = answer.Answer,
            CreatedAtUtc = assistantNow,
            Sequence = retryMessage is null ? nextSequence + 1 : nextSequence
        };
        for (var index = 0; index < answer.Sources.Count; index++)
        {
            var source = answer.Sources[index];
            assistantMessage.Sources.Add(new ConversationMessageSource
            {
                Id = Guid.NewGuid(),
                ConversationMessageId = assistantMessage.Id,
                SourceIndex = index + 1,
                SourceId = Truncate(source.SourceId, ConversationLimits.MaximumSourceIdLength),
                DocumentId = source.DocumentId,
                DocumentName = Truncate(source.DocumentName, ConversationLimits.MaximumDocumentNameLength),
                DocumentChunkId = source.ChunkId,
                ChunkIndex = source.ChunkIndex,
                PageStart = source.PageStart,
                PageEnd = source.PageEnd,
                Heading = source.Heading is null
                    ? null
                    : Truncate(source.Heading, ConversationLimits.MaximumHeadingLength),
                Excerpt = Truncate(source.Excerpt, ConversationLimits.MaximumSourceExcerptLength)
            });
        }

        conversation.Messages.Add(assistantMessage);
        _dbContext.ConversationMessages.Add(assistantMessage);
        conversation.UpdatedAtUtc = assistantNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Conversation message completed for project {ProjectId}, conversation {ConversationId}, with {HistoryMessageCount} recent context messages and {SourceCount} persisted source snapshots.",
            projectId,
            conversationId,
            precedingMessages.Length,
            assistantMessage.Sources.Count);
        return ToMessageResponse(assistantMessage);
    }

    private IQueryable<Conversation> OwnedConversationQuery(
        Guid ownerId,
        Guid projectId) =>
        _dbContext.Conversations.Where(conversation =>
            conversation.ProjectId == projectId &&
            conversation.Project.OwnerId == ownerId);

    private static ConversationResponse ToResponse(
        Conversation conversation,
        IEnumerable<ConversationMessage> messages) =>
        new(
            conversation.Id,
            conversation.ProjectId,
            conversation.Title,
            conversation.CreatedAtUtc,
            conversation.UpdatedAtUtc,
            messages.OrderBy(message => message.Sequence)
                .Select(ToMessageResponse)
                .ToArray());

    private static ConversationMessageResponse ToMessageResponse(
        ConversationMessage message) =>
        new(
            message.Id,
            message.Role.ToString(),
            message.Content,
            message.CreatedAtUtc,
            message.Sequence,
            message.Sources.OrderBy(source => source.SourceIndex)
                .Select(source => new ConversationMessageSourceResponse(
                    source.SourceId,
                    source.DocumentId,
                    source.DocumentName,
                    source.DocumentChunkId,
                    source.ChunkIndex,
                    source.PageStart,
                    source.PageEnd,
                    source.Heading,
                    source.Excerpt))
                .ToArray());

    internal static string GenerateTitle(string question)
    {
        var normalized = WhitespacePattern().Replace(question.Trim(), " ");
        return Truncate(normalized, ConversationLimits.GeneratedTitleLength);
    }

    private static string Truncate(string value, int maximumLength)
    {
        if (value.Length <= maximumLength)
        {
            return value;
        }

        var length = maximumLength - 1;
        if (length > 0 && char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value[..length].TrimEnd() + "…";
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
