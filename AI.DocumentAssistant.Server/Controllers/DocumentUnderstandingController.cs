using System.Security.Claims;
using AI.DocumentAssistant.Server.Contracts;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Understanding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/documents/{documentId:guid}/understanding")]
public sealed class DocumentUnderstandingController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IDocumentUnderstandingService _understandingService;
    private readonly ILogger<DocumentUnderstandingController> _logger;

    public DocumentUnderstandingController(
        ApplicationDbContext dbContext,
        IDocumentUnderstandingService understandingService,
        ILogger<DocumentUnderstandingController> logger)
    {
        _dbContext = dbContext;
        _understandingService = understandingService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<DocumentUnderstandingResponse>> Get(
        Guid projectId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return AuthenticationError();
        }

        var ownsDocument = await _dbContext.Documents
            .AsNoTracking()
            .AnyAsync(
                document =>
                    document.Id == documentId &&
                    document.ProjectId == projectId &&
                    document.Project.OwnerId == ownerId,
                cancellationToken);
        if (!ownsDocument)
        {
            return ResourceNotFound();
        }

        return Ok(await LoadResponseAsync(
            projectId,
            documentId,
            ownerId,
            cancellationToken));
    }

    [HttpPost("rebuild")]
    public async Task<ActionResult<DocumentUnderstandingResponse>> Rebuild(
        Guid projectId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return AuthenticationError();
        }

        var documentState = await _dbContext.Documents
            .AsNoTracking()
            .Where(document =>
                document.Id == documentId &&
                document.ProjectId == projectId &&
                document.Project.OwnerId == ownerId)
            .Select(document => new
            {
                document.Status,
                UnderstandingStatus = document.Understanding == null
                    ? (DocumentUnderstandingStatus?)null
                    : document.Understanding.Status,
                HasNormalizedText = document.TextSections.Any(section =>
                    section.NormalizedContent != null &&
                    section.NormalizedContent.Length > 0)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (documentState is null)
        {
            return ResourceNotFound();
        }

        if (documentState.Status != DocumentStatus.Ready)
        {
            return Conflict(new ApiErrorResponse(
                "Document understanding can be rebuilt only after processing is complete."));
        }

        if (!documentState.HasNormalizedText)
        {
            return Conflict(new ApiErrorResponse(
                "Normalized document text is required before understanding can be rebuilt."));
        }

        if (documentState.UnderstandingStatus == DocumentUnderstandingStatus.Processing)
        {
            return Conflict(new ApiErrorResponse(
                "Document understanding is already processing."));
        }

        try
        {
            await _understandingService.AnalyzePersistedAsync(
                documentId,
                force: true,
                cancellationToken);

            _logger.LogInformation(
                "User-requested document-understanding rebuild completed for document {DocumentId} in project {ProjectId}.",
                documentId,
                projectId);

            return Ok(await LoadResponseAsync(
                projectId,
                documentId,
                ownerId,
                cancellationToken));
        }
        catch (DocumentUnderstandingException exception)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(exception.SafeMessage));
        }
    }

    private async Task<DocumentUnderstandingResponse> LoadResponseAsync(
        Guid projectId,
        Guid documentId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var understanding = await _dbContext.DocumentUnderstandings
            .AsNoTracking()
            .Include(item => item.MetadataEntries)
            .SingleOrDefaultAsync(
                item =>
                    item.DocumentId == documentId &&
                    item.Document.ProjectId == projectId &&
                    item.Document.Project.OwnerId == ownerId,
                cancellationToken);

        if (understanding is null)
        {
            return new DocumentUnderstandingResponse(
                DocumentUnderstandingStatus.NotAnalyzed,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                [],
                null,
                null,
                null,
                null,
                null);
        }

        var hasCurrentResult = understanding.Status == DocumentUnderstandingStatus.Ready;
        return new DocumentUnderstandingResponse(
            understanding.Status,
            hasCurrentResult ? understanding.DocumentType : null,
            hasCurrentResult ? understanding.DocumentSubtype : null,
            hasCurrentResult ? understanding.DocumentTypeConfidence : null,
            hasCurrentResult ? understanding.PrimaryLanguageCode : null,
            hasCurrentResult ? understanding.LanguageConfidence : null,
            hasCurrentResult ? understanding.DetectedTitle : null,
            hasCurrentResult ? understanding.Subject : null,
            hasCurrentResult
                ? understanding.MetadataEntries
                    .OrderBy(entry => entry.Sequence)
                    .Select(entry => new DocumentMetadataEntryResponse(
                        entry.Kind,
                        entry.Label,
                        entry.Value,
                        entry.NormalizedValue,
                        entry.Confidence,
                        entry.Sequence))
                    .ToArray()
                : [],
            understanding.Model,
            understanding.PromptVersion,
            understanding.SourceContentHash,
            understanding.AnalyzedAtUtc,
            understanding.LastError);
    }

    private bool TryGetOwnerId(out Guid ownerId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out ownerId);

    private UnauthorizedObjectResult AuthenticationError() =>
        Unauthorized(new ApiErrorResponse("Authentication is required."));

    private NotFoundObjectResult ResourceNotFound() =>
        NotFound(new ApiErrorResponse("Project or document not found."));
}
