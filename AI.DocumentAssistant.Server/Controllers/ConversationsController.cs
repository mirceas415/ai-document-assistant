using System.Security.Claims;
using AI.DocumentAssistant.Server.Contracts;
using AI.DocumentAssistant.Server.Conversations;
using AI.DocumentAssistant.Server.Embeddings;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Rag;
using AI.DocumentAssistant.Server.Retrieval;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI.DocumentAssistant.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/conversations")]
public sealed class ConversationsController : ControllerBase
{
    private readonly IConversationService _conversationService;

    public ConversationsController(IConversationService conversationService)
    {
        _conversationService = conversationService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ConversationSummaryResponse>>> List(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return UnauthorizedResponse();
        }

        var conversations = await _conversationService.ListAsync(
            ownerId,
            projectId,
            cancellationToken);
        return conversations is null ? NotFoundResponse() : Ok(conversations);
    }

    [HttpPost]
    public async Task<ActionResult<ConversationResponse>> Create(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return UnauthorizedResponse();
        }

        var conversation = await _conversationService.CreateAsync(
            ownerId,
            projectId,
            cancellationToken);
        return conversation is null
            ? NotFoundResponse()
            : CreatedAtAction(
                nameof(Get),
                new { projectId, conversationId = conversation.Id },
                conversation);
    }

    [HttpGet("{conversationId:guid}")]
    public async Task<ActionResult<ConversationResponse>> Get(
        Guid projectId,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return UnauthorizedResponse();
        }

        var conversation = await _conversationService.GetAsync(
            ownerId,
            projectId,
            conversationId,
            cancellationToken);
        return conversation is null ? NotFoundResponse() : Ok(conversation);
    }

    [HttpPatch("{conversationId:guid}")]
    public async Task<ActionResult<ConversationResponse>> Rename(
        Guid projectId,
        Guid conversationId,
        RenameConversationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return UnauthorizedResponse();
        }

        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            return ValidationError("title", "A conversation title is required.");
        }

        if (title.Length > ConversationLimits.MaximumTitleLength)
        {
            return ValidationError(
                "title",
                $"The title cannot exceed {ConversationLimits.MaximumTitleLength} characters.");
        }

        var conversation = await _conversationService.RenameAsync(
            ownerId,
            projectId,
            conversationId,
            title,
            cancellationToken);
        return conversation is null ? NotFoundResponse() : Ok(conversation);
    }

    [HttpDelete("{conversationId:guid}")]
    public async Task<IActionResult> Delete(
        Guid projectId,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return UnauthorizedResponse();
        }

        var deleted = await _conversationService.DeleteAsync(
            ownerId,
            projectId,
            conversationId,
            cancellationToken);
        return deleted ? NoContent() : NotFoundResponse();
    }

    [HttpPost("{conversationId:guid}/messages")]
    public async Task<ActionResult<ConversationMessageResponse>> Ask(
        Guid projectId,
        Guid conversationId,
        CreateConversationMessageRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return UnauthorizedResponse();
        }

        var question = request.Question?.Trim() ?? string.Empty;
        if (question.Length == 0)
        {
            return ValidationError("question", "A question is required.");
        }

        if (question.Length > SemanticRetrievalLimits.MaximumQueryLength)
        {
            return ValidationError(
                "question",
                $"The question cannot exceed {SemanticRetrievalLimits.MaximumQueryLength:N0} characters.");
        }

        try
        {
            var message = await _conversationService.AskAsync(
                ownerId,
                projectId,
                conversationId,
                question,
                request.RetryMessageId,
                cancellationToken);
            return message is null ? NotFoundResponse() : Ok(message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DocumentEmbeddingException)
        {
            return ServiceUnavailable();
        }
        catch (SemanticRetrievalException)
        {
            return ServiceUnavailable();
        }
        catch (GroundedAnswerException)
        {
            return ServiceUnavailable();
        }
    }

    private bool TryGetOwnerId(out Guid ownerId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out ownerId);

    private UnauthorizedObjectResult UnauthorizedResponse() =>
        Unauthorized(new ApiErrorResponse("Authentication is required."));

    private NotFoundObjectResult NotFoundResponse() =>
        NotFound(new ApiErrorResponse("Conversation not found."));

    private BadRequestObjectResult ValidationError(string field, string error) =>
        BadRequest(new ApiErrorResponse(
            "Validation failed.",
            new Dictionary<string, string[]> { [field] = [error] }));

    private ObjectResult ServiceUnavailable() =>
        StatusCode(
            StatusCodes.Status503ServiceUnavailable,
            new ApiErrorResponse(
                "An answer could not be generated right now. Your question was saved; please try again."));
}
