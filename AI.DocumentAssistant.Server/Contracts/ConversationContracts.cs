using System.ComponentModel.DataAnnotations;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Retrieval;

namespace AI.DocumentAssistant.Server.Contracts;

public sealed record ConversationSummaryResponse(
    Guid Id,
    string Title,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int MessageCount,
    int SourceCount);

public sealed record ConversationResponse(
    Guid Id,
    Guid ProjectId,
    string Title,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<ConversationMessageResponse> Messages);

public sealed record ConversationMessageResponse(
    Guid Id,
    string Role,
    string Content,
    DateTime CreatedAtUtc,
    int Sequence,
    IReadOnlyList<ConversationMessageSourceResponse> Sources);

public sealed record ConversationMessageSourceResponse(
    string SourceId,
    Guid? DocumentId,
    string DocumentName,
    Guid? DocumentChunkId,
    int ChunkIndex,
    int? PageStart,
    int? PageEnd,
    string? Heading,
    string Excerpt);

public sealed class RenameConversationRequest
{
    [Required]
    [MaxLength(ConversationLimits.MaximumTitleLength)]
    public string? Title { get; init; }
}

public sealed class CreateConversationMessageRequest
{
    [Required]
    [MaxLength(SemanticRetrievalLimits.MaximumQueryLength)]
    public string? Question { get; init; }

    // Used only for a user-triggered retry of the last persisted failed question.
    // It never accepts or supplies source mappings from the client.
    public Guid? RetryMessageId { get; init; }
}
