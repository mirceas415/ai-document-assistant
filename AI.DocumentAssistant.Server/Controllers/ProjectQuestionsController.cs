using System.Security.Claims;
using AI.DocumentAssistant.Server.Contracts;
using AI.DocumentAssistant.Server.Embeddings;
using AI.DocumentAssistant.Server.Rag;
using AI.DocumentAssistant.Server.Retrieval;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI.DocumentAssistant.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}")]
public sealed class ProjectQuestionsController : ControllerBase
{
    private readonly IProjectQuestionAnsweringService _questionAnsweringService;

    public ProjectQuestionsController(
        IProjectQuestionAnsweringService questionAnsweringService)
    {
        _questionAnsweringService = questionAnsweringService;
    }

    [HttpPost("ask")]
    public async Task<ActionResult<AskProjectResponse>> Ask(
        Guid projectId,
        AskProjectRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var ownerId))
        {
            return Unauthorized(new ApiErrorResponse("Authentication is required."));
        }

        var question = request.Question?.Trim() ?? string.Empty;
        if (question.Length == 0)
        {
            return ValidationError("A question is required.");
        }

        if (question.Length > SemanticRetrievalLimits.MaximumQueryLength)
        {
            return ValidationError(
                $"The question cannot exceed {SemanticRetrievalLimits.MaximumQueryLength:N0} characters.");
        }

        try
        {
            var answer = await _questionAnsweringService.AnswerAsync(
                ownerId,
                projectId,
                question,
                cancellationToken);
            if (answer is null)
            {
                return NotFound(new ApiErrorResponse("Project not found."));
            }

            return Ok(new AskProjectResponse(
                answer.Answer,
                answer.Sources.Select(source => new AskProjectSourceResponse(
                    source.SourceId,
                    source.DocumentId,
                    source.DocumentName,
                    source.ChunkId,
                    source.ChunkIndex,
                    source.PageStart,
                    source.PageEnd,
                    source.Heading,
                    source.Excerpt)).ToArray()));
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

    private BadRequestObjectResult ValidationError(string error) =>
        BadRequest(new ApiErrorResponse(
            "Validation failed.",
            new Dictionary<string, string[]> { ["question"] = [error] }));

    private ObjectResult ServiceUnavailable() =>
        StatusCode(
            StatusCodes.Status503ServiceUnavailable,
            new ApiErrorResponse(
                "An answer could not be generated right now. Please try again."));
}
