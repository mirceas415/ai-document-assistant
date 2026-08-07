using System.Security.Claims;
using AI.DocumentAssistant.Server.Contracts;
using AI.DocumentAssistant.Server.Embeddings;
using AI.DocumentAssistant.Server.Retrieval;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI.DocumentAssistant.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}")]
public sealed class ProjectIntelligenceController : ControllerBase
{
    private readonly ISemanticRetrievalService _retrievalService;

    public ProjectIntelligenceController(ISemanticRetrievalService retrievalService)
    {
        _retrievalService = retrievalService;
    }

    [HttpPost("search")]
    public async Task<ActionResult<SemanticSearchResponse>> Search(
        Guid projectId,
        SemanticSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return Unauthorized(new ApiErrorResponse("Authentication is required."));
        }

        var query = request.Query?.Trim() ?? string.Empty;
        var topK = request.TopK ?? SemanticRetrievalLimits.DefaultTopK;
        var errors = Validate(query, topK);
        if (errors.Count > 0)
        {
            return BadRequest(new ApiErrorResponse("Validation failed.", errors));
        }

        try
        {
            var retrieval = await _retrievalService.SearchAsync(
                ownerId,
                projectId,
                query,
                topK,
                cancellationToken);
            if (retrieval is null)
            {
                return NotFound(new ApiErrorResponse("Project not found."));
            }

            return Ok(new SemanticSearchResponse(
                retrieval.TopK,
                retrieval.Chunks
                    .Select(chunk => new SemanticSearchResultResponse(
                        chunk.DocumentId,
                        chunk.DocumentName,
                        chunk.ChunkId,
                        chunk.ChunkIndex,
                        chunk.Content,
                        chunk.PageStart,
                        chunk.PageEnd,
                        chunk.Heading,
                        chunk.CosineDistance))
                    .ToArray()));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DocumentEmbeddingException)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ApiErrorResponse(
                    "Semantic search is temporarily unavailable. Please try again."));
        }
        catch (SemanticRetrievalException exception)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ApiErrorResponse(exception.SafeMessage));
        }
    }

    private bool TryGetOwnerId(out Guid ownerId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out ownerId);

    private static Dictionary<string, string[]> Validate(string query, int topK)
    {
        var errors = new Dictionary<string, string[]>();

        if (query.Length == 0)
        {
            errors["query"] = ["A search query is required."];
        }
        else if (query.Length > SemanticRetrievalLimits.MaximumQueryLength)
        {
            errors["query"] =
            [
                $"The search query cannot exceed {SemanticRetrievalLimits.MaximumQueryLength:N0} characters."
            ];
        }

        if (topK is < 1 or > SemanticRetrievalLimits.MaximumTopK)
        {
            errors["topK"] =
            [
                $"TopK must be between 1 and {SemanticRetrievalLimits.MaximumTopK}."
            ];
        }

        return errors;
    }
}
