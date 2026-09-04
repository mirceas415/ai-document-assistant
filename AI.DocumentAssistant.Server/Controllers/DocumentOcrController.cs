using System.Security.Claims;
using AI.DocumentAssistant.Server.Contracts;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Ocr;
using AI.DocumentAssistant.Server.Processing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/documents/{documentId:guid}/ocr")]
public sealed class DocumentOcrController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IDocumentProcessingService _processingService;
    private readonly OcrRoutingPolicy _routingPolicy;
    private readonly ILogger<DocumentOcrController> _logger;

    public DocumentOcrController(
        ApplicationDbContext dbContext,
        IDocumentProcessingService processingService,
        OcrRoutingPolicy routingPolicy,
        ILogger<DocumentOcrController> logger)
    {
        _dbContext = dbContext;
        _processingService = processingService;
        _routingPolicy = routingPolicy;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<DocumentOcrAnalysisResponse>> Get(
        Guid projectId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return AuthenticationError();
        }

        var document = await LoadOwnedDocumentAsync(
            projectId,
            documentId,
            ownerId,
            cancellationToken);
        return document is null
            ? ResourceNotFound()
            : Ok(ToResponse(document));
    }

    [HttpPost("rebuild")]
    public async Task<ActionResult<DocumentOcrAnalysisResponse>> Rebuild(
        Guid projectId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return AuthenticationError();
        }

        var state = await _dbContext.Documents
            .AsNoTracking()
            .Where(document =>
                document.Id == documentId &&
                document.ProjectId == projectId &&
                document.Project.OwnerId == ownerId)
            .Select(document => new
            {
                document.ContentType,
                document.Status,
                UnderstandingStatus = document.Understanding == null
                    ? (DocumentUnderstandingStatus?)null
                    : document.Understanding.Status,
                TechnicalStatus = document.TechnicalAnalysis == null
                    ? (DocumentTechnicalAnalysisStatus?)null
                    : document.TechnicalAnalysis.Status,
                OcrStatus = document.OcrAnalysis == null
                    ? (DocumentOcrStatus?)null
                    : document.OcrAnalysis.Status
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (state is null)
        {
            return ResourceNotFound();
        }

        if (!OcrArchitecture.IsPdf(state.ContentType))
        {
            return BadRequest(new ApiErrorResponse(
                "Local OCR is applicable only to PDF documents."));
        }

        if (state.Status == DocumentStatus.Processing ||
            state.UnderstandingStatus == DocumentUnderstandingStatus.Processing ||
            state.TechnicalStatus == DocumentTechnicalAnalysisStatus.Processing ||
            state.OcrStatus == DocumentOcrStatus.Processing)
        {
            return Conflict(new ApiErrorResponse(
                "OCR cannot be rebuilt while the document is processing or being rebuilt."));
        }

        if (state.Status is not (DocumentStatus.Ready or DocumentStatus.Failed))
        {
            return Conflict(new ApiErrorResponse(
                "OCR can be rebuilt after initial document processing has completed."));
        }

        try
        {
            await _processingService.RebuildOcrAsync(documentId, cancellationToken);
            var refreshed = await LoadOwnedDocumentAsync(
                projectId,
                documentId,
                ownerId,
                cancellationToken);
            if (refreshed is null)
            {
                return ResourceNotFound();
            }

            _logger.LogInformation(
                "User-requested local OCR rebuild completed for document {DocumentId} in project {ProjectId} with OCR status {OcrStatus}.",
                documentId,
                projectId,
                refreshed.OcrAnalysis?.Status);
            return Ok(ToResponse(refreshed));
        }
        catch (DocumentExtractionException exception)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(exception.SafeMessage));
        }
    }

    private Task<Document?> LoadOwnedDocumentAsync(
        Guid projectId,
        Guid documentId,
        Guid ownerId,
        CancellationToken cancellationToken) =>
        _dbContext.Documents
            .AsNoTracking()
            .Include(document => document.OcrAnalysis!)
                .ThenInclude(analysis => analysis.Pages)
            .Include(document => document.TechnicalAnalysis!)
                .ThenInclude(analysis => analysis.Pages)
            .SingleOrDefaultAsync(
                document =>
                    document.Id == documentId &&
                    document.ProjectId == projectId &&
                    document.Project.OwnerId == ownerId,
                cancellationToken);

    private DocumentOcrAnalysisResponse ToResponse(Document document)
    {
        if (!OcrArchitecture.IsPdf(document.ContentType))
        {
            return EmptyResponse(DocumentOcrStatus.Skipped, null);
        }

        var analysis = document.OcrAnalysis;
        if (analysis is null)
        {
            return EmptyResponse(DocumentOcrStatus.NotAnalyzed, null);
        }

        if (analysis.Status != DocumentOcrStatus.Processing &&
            document.TechnicalAnalysis?.Status == DocumentTechnicalAnalysisStatus.Ready)
        {
            var candidates = _routingPolicy.SelectCandidates(document.TechnicalAnalysis.Pages);
            var currentRoutingHash = _routingPolicy.ComputeRoutingHash(candidates);
            if (!string.Equals(
                    analysis.SourceFileHash,
                    document.TechnicalAnalysis.SourceFileHash,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    analysis.RoutingVersion,
                    _routingPolicy.Version,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    analysis.RoutingHash,
                    currentRoutingHash,
                    StringComparison.Ordinal))
            {
                return EmptyResponse(
                    DocumentOcrStatus.NotAnalyzed,
                    "OCR must be rebuilt for the current technical PDF analysis.");
            }
        }

        return new DocumentOcrAnalysisResponse(
            analysis.Status,
            analysis.CandidatePageCount,
            analysis.SuccessfulPageCount,
            analysis.FailedPageCount,
            analysis.EngineName,
            analysis.EngineVersion,
            analysis.Languages,
            analysis.RenderDpi,
            analysis.MaxCandidatePages,
            analysis.MaxRenderedPixels,
            analysis.SourceFileHash,
            analysis.RoutingVersion,
            analysis.RoutingHash,
            analysis.ConfigurationHash,
            analysis.ProcessedAtUtc,
            analysis.LastError,
            analysis.Pages
                .OrderBy(page => page.PageNumber)
                .Select(page => new DocumentPageOcrResultResponse(
                    page.PageNumber,
                    page.Status,
                    page.SourceTechnicalType,
                    page.RecognizedCharacterCount,
                    page.RecognizedWordCount,
                    page.MeanConfidence,
                    page.EffectiveRenderDpi,
                    page.RenderedWidthPixels,
                    page.RenderedHeightPixels,
                    page.ProcessingDurationMs,
                    page.UsedInExtraction,
                    page.LastError))
                .ToArray());
    }

    private DocumentOcrAnalysisResponse EmptyResponse(
        DocumentOcrStatus status,
        string? lastError) =>
        new(
            status,
            0,
            0,
            0,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            _routingPolicy.Version,
            null,
            null,
            null,
            lastError,
            []);

    private bool TryGetOwnerId(out Guid ownerId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out ownerId);

    private UnauthorizedObjectResult AuthenticationError() =>
        Unauthorized(new ApiErrorResponse("Authentication is required."));

    private NotFoundObjectResult ResourceNotFound() =>
        NotFound(new ApiErrorResponse("Project or document not found."));
}
