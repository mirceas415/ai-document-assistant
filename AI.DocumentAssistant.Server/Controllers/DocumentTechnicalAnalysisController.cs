using System.Security.Claims;
using AI.DocumentAssistant.Server.Contracts;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.TechnicalAnalysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/documents/{documentId:guid}/technical-analysis")]
public sealed class DocumentTechnicalAnalysisController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IDocumentTechnicalAnalysisService _technicalAnalysisService;
    private readonly ILogger<DocumentTechnicalAnalysisController> _logger;

    public DocumentTechnicalAnalysisController(
        ApplicationDbContext dbContext,
        IDocumentTechnicalAnalysisService technicalAnalysisService,
        ILogger<DocumentTechnicalAnalysisController> logger)
    {
        _dbContext = dbContext;
        _technicalAnalysisService = technicalAnalysisService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<DocumentTechnicalAnalysisResponse>> Get(
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
        if (document is null)
        {
            return ResourceNotFound();
        }

        return Ok(ToResponse(document));
    }

    [HttpPost("rebuild")]
    public async Task<ActionResult<DocumentTechnicalAnalysisResponse>> Rebuild(
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
                document.ContentType,
                TechnicalAnalysisStatus = document.TechnicalAnalysis == null
                    ? (DocumentTechnicalAnalysisStatus?)null
                    : document.TechnicalAnalysis.Status
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (documentState is null)
        {
            return ResourceNotFound();
        }

        if (!PdfTechnicalAnalysisArchitecture.IsPdf(documentState.ContentType))
        {
            return BadRequest(new ApiErrorResponse(
                PdfTechnicalAnalysisArchitecture.NotApplicableMessage));
        }

        if (documentState.TechnicalAnalysisStatus ==
            DocumentTechnicalAnalysisStatus.Processing)
        {
            return Conflict(new ApiErrorResponse(
                "Technical PDF analysis is already processing."));
        }

        try
        {
            await _technicalAnalysisService.AnalyzeAsync(
                documentId,
                force: true,
                cancellationToken);

            _logger.LogInformation(
                "User-requested technical PDF analysis rebuild completed for document {DocumentId} in project {ProjectId}.",
                documentId,
                projectId);

            var refreshed = await LoadOwnedDocumentAsync(
                projectId,
                documentId,
                ownerId,
                cancellationToken);
            return refreshed is null
                ? ResourceNotFound()
                : Ok(ToResponse(refreshed));
        }
        catch (PdfTechnicalAnalysisException exception)
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
            .Include(document => document.TechnicalAnalysis!)
                .ThenInclude(analysis => analysis.Pages)
            .SingleOrDefaultAsync(
                document =>
                    document.Id == documentId &&
                    document.ProjectId == projectId &&
                    document.Project.OwnerId == ownerId,
                cancellationToken);

    private static DocumentTechnicalAnalysisResponse ToResponse(Document document)
    {
        var analysis = document.TechnicalAnalysis;
        if (analysis is null)
        {
            var status = PdfTechnicalAnalysisArchitecture.IsPdf(document.ContentType)
                ? DocumentTechnicalAnalysisStatus.NotAnalyzed
                : DocumentTechnicalAnalysisStatus.Skipped;
            return new DocumentTechnicalAnalysisResponse(
                status,
                TechnicalType.Unknown,
                0,
                0,
                0,
                0,
                0,
                0,
                null,
                null,
                null,
                null,
                []);
        }

        var hasCurrentResult =
            analysis.Status == DocumentTechnicalAnalysisStatus.Ready;
        return new DocumentTechnicalAnalysisResponse(
            analysis.Status,
            hasCurrentResult ? analysis.TechnicalType : TechnicalType.Unknown,
            hasCurrentResult ? analysis.PageCount : 0,
            hasCurrentResult ? analysis.TextBasedPageCount : 0,
            hasCurrentResult ? analysis.ScannedPageCount : 0,
            hasCurrentResult ? analysis.ImageBasedPageCount : 0,
            hasCurrentResult ? analysis.MixedPageCount : 0,
            hasCurrentResult ? analysis.UnknownPageCount : 0,
            analysis.SourceFileHash,
            analysis.AnalyzerVersion,
            analysis.AnalyzedAtUtc,
            analysis.LastError,
            hasCurrentResult
                ? analysis.Pages
                    .OrderBy(page => page.PageNumber)
                    .Select(page => new DocumentPageTechnicalAnalysisResponse(
                        page.PageNumber,
                        page.TechnicalType,
                        page.TextCharacterCount,
                        page.WordCount,
                        page.ImageCount,
                        page.ImageCoverageRatio,
                        page.HasMeaningfulText,
                        page.HasPageSizedImage))
                    .ToArray()
                : []);
    }

    private bool TryGetOwnerId(out Guid ownerId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out ownerId);

    private UnauthorizedObjectResult AuthenticationError() =>
        Unauthorized(new ApiErrorResponse("Authentication is required."));

    private NotFoundObjectResult ResourceNotFound() =>
        NotFound(new ApiErrorResponse("Project or document not found."));
}
