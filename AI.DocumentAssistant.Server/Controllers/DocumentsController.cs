using System.IO.Compression;
using System.Security.Claims;
using AI.DocumentAssistant.Server.Chunking;
using AI.DocumentAssistant.Server.Contracts;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Embeddings;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Normalization;
using AI.DocumentAssistant.Server.Ocr;
using AI.DocumentAssistant.Server.Processing;
using AI.DocumentAssistant.Server.Storage;
using AI.DocumentAssistant.Server.TechnicalAnalysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/documents")]
public sealed class DocumentsController : ControllerBase
{
    private const long MaxFileSizeBytes = 20L * 1024 * 1024;
    private const long MaxRequestSizeBytes = MaxFileSizeBytes + (64 * 1024);

    private static readonly IReadOnlyDictionary<string, string> AllowedFileTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        };

    private static readonly byte[] PdfSignature = [0x25, 0x50, 0x44, 0x46, 0x2D];
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    private readonly ApplicationDbContext _dbContext;
    private readonly IFileStorageService _fileStorage;
    private readonly IDocumentProcessingQueue _processingQueue;
    private readonly IDocumentChunkingService _chunkingService;
    private readonly IDocumentNormalizationService _normalizationService;
    private readonly IDocumentEmbeddingService _embeddingService;
    private readonly OpenAIEmbeddingOptions _embeddingOptions;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        ApplicationDbContext dbContext,
        IFileStorageService fileStorage,
        IDocumentProcessingQueue processingQueue,
        IDocumentChunkingService chunkingService,
        IDocumentNormalizationService normalizationService,
        IDocumentEmbeddingService embeddingService,
        IOptions<OpenAIEmbeddingOptions> embeddingOptions,
        ILogger<DocumentsController> logger)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _processingQueue = processingQueue;
        _chunkingService = chunkingService;
        _normalizationService = normalizationService;
        _embeddingService = embeddingService;
        _embeddingOptions = embeddingOptions.Value;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DocumentSummary>>> GetAll(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return AuthenticationError();
        }

        var ownsProject = await _dbContext.Projects
            .AsNoTracking()
            .AnyAsync(
                project => project.Id == projectId && project.OwnerId == ownerId,
                cancellationToken);

        if (!ownsProject)
        {
            return ResourceNotFound();
        }

        var documents = await _dbContext.Documents
            .AsNoTracking()
            .Include(document => document.Understanding)
            .Include(document => document.TechnicalAnalysis)
            .Include(document => document.OcrAnalysis)
            .Where(document =>
                document.ProjectId == projectId &&
                document.Project.OwnerId == ownerId)
            .OrderByDescending(document => document.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var embeddingCurrency = await LoadEmbeddingCurrencyAsync(
            documents,
            ownerId,
            cancellationToken);

        return Ok(documents
            .Select(document => ToSummary(
                document,
                embeddingCurrency.GetValueOrDefault(document.Id)))
            .ToArray());
    }

    [HttpGet("{documentId:guid}")]
    public async Task<ActionResult<DocumentDetails>> GetById(
        Guid projectId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return AuthenticationError();
        }

        var document = await _dbContext.Documents
            .AsNoTracking()
            .Include(document => document.Understanding)
            .Include(document => document.TechnicalAnalysis)
            .Include(document => document.OcrAnalysis)
            .SingleOrDefaultAsync(
                document =>
                    document.Id == documentId &&
                    document.ProjectId == projectId &&
                    document.Project.OwnerId == ownerId,
                cancellationToken);

        if (document is null)
        {
            return ResourceNotFound();
        }

        var embeddingCurrency = await LoadEmbeddingCurrencyAsync(
            [document],
            ownerId,
            cancellationToken);

        return Ok(ToDetails(
            document,
            embeddingCurrency.GetValueOrDefault(document.Id)));
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestSizeBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestSizeBytes)]
    public async Task<ActionResult<DocumentDetails>> Upload(
        Guid projectId,
        [FromForm] UploadDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return AuthenticationError();
        }

        var ownsProject = await _dbContext.Projects
            .AsNoTracking()
            .AnyAsync(
                project => project.Id == projectId && project.OwnerId == ownerId,
                cancellationToken);

        if (!ownsProject)
        {
            return ResourceNotFound();
        }

        var validation = await ValidateFileAsync(request.File, cancellationToken);

        if (validation.Error is not null)
        {
            return FileValidationError(validation.Error);
        }

        var validatedFile = validation.File!;
        string? storedFileName = null;

        try
        {
            await using var source = request.File!.OpenReadStream();
            storedFileName = await _fileStorage.SaveAsync(
                source,
                validatedFile.Extension,
                cancellationToken);

            var now = DateTime.UtcNow;
            var document = new Document
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                OriginalFileName = validatedFile.OriginalFileName,
                StoredFileName = storedFileName,
                ContentType = validatedFile.ContentType,
                FileSizeBytes = request.File.Length,
                Status = DocumentStatus.Uploaded,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            document.Understanding = new DocumentUnderstanding
            {
                DocumentId = document.Id,
                Status = DocumentUnderstandingStatus.Pending
            };
            document.TechnicalAnalysis = new DocumentTechnicalAnalysis
            {
                DocumentId = document.Id,
                Status = PdfTechnicalAnalysisArchitecture.IsPdf(document.ContentType)
                    ? DocumentTechnicalAnalysisStatus.NotAnalyzed
                    : DocumentTechnicalAnalysisStatus.Skipped,
                TechnicalType = TechnicalType.Unknown,
                AnalyzerVersion = PdfTechnicalAnalysisArchitecture.AnalyzerVersion,
                AnalyzedAtUtc = PdfTechnicalAnalysisArchitecture.IsPdf(document.ContentType)
                    ? null
                    : now
            };
            document.OcrAnalysis = new DocumentOcrAnalysis
            {
                DocumentId = document.Id,
                Status = OcrArchitecture.IsPdf(document.ContentType)
                    ? DocumentOcrStatus.NotAnalyzed
                    : DocumentOcrStatus.Skipped,
                RoutingVersion = OcrArchitecture.RoutingVersion,
                ProcessedAtUtc = OcrArchitecture.IsPdf(document.ContentType)
                    ? null
                    : now
            };

            _dbContext.Documents.Add(document);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (!_processingQueue.TryEnqueue(document.Id))
            {
                _logger.LogWarning(
                    "Document {DocumentId} in project {ProjectId} was uploaded but could not be queued because the in-memory processing queue is full.",
                    document.Id,
                    document.ProjectId);
            }

            var response = ToDetails(document);
            return CreatedAtAction(
                nameof(GetById),
                new { projectId, documentId = document.Id },
                response);
        }
        catch
        {
            if (storedFileName is not null)
            {
                try
                {
                    await _fileStorage.DeleteAsync(storedFileName, CancellationToken.None);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogWarning(
                        cleanupException,
                        "Could not remove stored file {StoredFileName} after a failed document upload.",
                        storedFileName);
                }
            }

            throw;
        }
    }

    [HttpPost("{documentId:guid}/process")]
    public async Task<IActionResult> Process(
        Guid projectId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return AuthenticationError();
        }

        var document = await _dbContext.Documents
            .SingleOrDefaultAsync(
                item =>
                    item.Id == documentId &&
                    item.ProjectId == projectId &&
                    item.Project.OwnerId == ownerId,
                cancellationToken);

        if (document is null)
        {
            return ResourceNotFound();
        }

        if (document.Status == DocumentStatus.Processing)
        {
            return Conflict(new ApiErrorResponse("The document is already processing."));
        }

        if (document.Status == DocumentStatus.Ready)
        {
            return Conflict(new ApiErrorResponse("The document has already been processed."));
        }

        var previousStatus = document.Status;
        var previousError = document.ProcessingError;
        var previousChunkingError = document.ChunkingError;
        var previousNormalizationError = document.NormalizationError;
        var previousEmbeddingError = document.EmbeddingError;

        if (document.Status == DocumentStatus.Failed)
        {
            document.Status = DocumentStatus.Uploaded;
            document.ProcessingError = null;
            document.ChunkingError = null;
            document.NormalizationError = null;
            document.EmbeddingError = null;
            document.UpdatedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        if (!_processingQueue.TryEnqueue(document.Id))
        {
            if (previousStatus == DocumentStatus.Failed)
            {
                document.Status = previousStatus;
                document.ProcessingError = previousError;
                document.ChunkingError = previousChunkingError;
                document.NormalizationError = previousNormalizationError;
                document.EmbeddingError = previousEmbeddingError;
                document.UpdatedAtUtc = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            _logger.LogWarning(
                "Manual processing request for document {DocumentId} in project {ProjectId} could not be queued because the in-memory queue is full.",
                document.Id,
                document.ProjectId);

            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ApiErrorResponse("Document processing is temporarily busy. Please retry."));
        }

        _logger.LogInformation(
            "Document {DocumentId} in project {ProjectId} was queued manually for processing.",
            document.Id,
            document.ProjectId);

        return Accepted(new { message = "Document processing was queued." });
    }

    [HttpGet("{documentId:guid}/text")]
    public async Task<ActionResult<IReadOnlyList<ExtractedTextSectionResponse>>> GetText(
        Guid projectId,
        Guid documentId,
        CancellationToken cancellationToken,
        [FromQuery] string view = "raw")
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return AuthenticationError();
        }

        var normalizedView = string.Equals(view, "normalized", StringComparison.OrdinalIgnoreCase);
        if (!normalizedView && !string.Equals(view, "raw", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new ApiErrorResponse(
                "The text view must be either 'raw' or 'normalized'."));
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
                HasNormalization = document.NormalizedAtUtc != null
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (documentState is null)
        {
            return ResourceNotFound();
        }

        if (documentState.Status != DocumentStatus.Ready)
        {
            return Conflict(new ApiErrorResponse(
                "Extracted text is available only after processing is complete."));
        }

        if (normalizedView && !documentState.HasNormalization)
        {
            return Conflict(new ApiErrorResponse(
                "Normalized text is not available. Rebuild normalization first."));
        }

        var sections = await _dbContext.DocumentTextSections
            .AsNoTracking()
            .Where(section =>
                section.DocumentId == documentId &&
                section.Document.ProjectId == projectId &&
                section.Document.Project.OwnerId == ownerId)
            .OrderBy(section => section.SectionIndex)
            .Select(section => new ExtractedTextSectionResponse(
                section.SectionIndex,
                section.PageNumber,
                section.SectionTitle,
                normalizedView ? section.NormalizedContent! : section.Content,
                section.Content.Length,
                section.NormalizedContent == null ? null : section.NormalizedContent.Length,
                section.RemovedCharacterCount,
                section.NormalizationChanged,
                section.NormalizedAtUtc,
                section.ExtractionMethod))
            .ToListAsync(cancellationToken);

        return Ok(sections);
    }

    [HttpPost("{documentId:guid}/normalization/rebuild")]
    public async Task<ActionResult<DocumentDetails>> RebuildNormalization(
        Guid projectId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return AuthenticationError();
        }

        var document = await _dbContext.Documents
            .SingleOrDefaultAsync(
                item =>
                    item.Id == documentId &&
                    item.ProjectId == projectId &&
                    item.Project.OwnerId == ownerId,
                cancellationToken);

        if (document is null)
        {
            return ResourceNotFound();
        }

        if (document.Status == DocumentStatus.Processing)
        {
            return Conflict(new ApiErrorResponse(
                "Normalization cannot be rebuilt while the document is processing."));
        }

        if (document.Status != DocumentStatus.Ready)
        {
            return Conflict(new ApiErrorResponse(
                "Normalization can be rebuilt only after processing is complete."));
        }

        var hasSourceSections = await _dbContext.DocumentTextSections
            .AsNoTracking()
            .AnyAsync(section => section.DocumentId == documentId, cancellationToken);
        if (!hasSourceSections)
        {
            return Conflict(new ApiErrorResponse(
                "Extracted text is required before normalization can be rebuilt."));
        }

        if (!await TryClaimRebuildAsync(
                document,
                ownerId,
                cancellationToken))
        {
            return Conflict(new ApiErrorResponse(
                "The document is already being processed or rebuilt."));
        }

        try
        {
            var result = await _normalizationService.RebuildAsync(
                documentId,
                cancellationToken);
            var refreshedDocument = await _dbContext.Documents
                .AsNoTracking()
                .Include(item => item.Understanding)
                .Include(item => item.TechnicalAnalysis)
                .Include(item => item.OcrAnalysis)
                .SingleAsync(item => item.Id == documentId, cancellationToken);

            _logger.LogInformation(
                "User-requested normalization rebuild completed for document {DocumentId} in project {ProjectId}; changed {ChangedSectionCount} sections, removed {RemovedCharacterCount} characters, and generated {ChunkCount} chunks.",
                documentId,
                projectId,
                result.ChangedSectionCount,
                result.RemovedCharacterCount,
                result.ChunkCount);

            return Ok(ToDetails(refreshedDocument, embeddingsAreCurrent: true));
        }
        catch (DocumentEmbeddingException exception)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(exception.SafeMessage));
        }
        catch (DocumentNormalizationException exception)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(exception.SafeMessage));
        }
    }

    [HttpGet("{documentId:guid}/chunks")]
    public async Task<ActionResult<IReadOnlyList<DocumentChunkResponse>>> GetChunks(
        Guid projectId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return AuthenticationError();
        }

        var status = await _dbContext.Documents
            .AsNoTracking()
            .Where(document =>
                document.Id == documentId &&
                document.ProjectId == projectId &&
                document.Project.OwnerId == ownerId)
            .Select(document => (DocumentStatus?)document.Status)
            .SingleOrDefaultAsync(cancellationToken);

        if (status is null)
        {
            return ResourceNotFound();
        }

        if (status != DocumentStatus.Ready)
        {
            return Conflict(new ApiErrorResponse(
                "Document chunks are available only after processing is complete."));
        }

        var chunks = await _dbContext.DocumentChunks
            .AsNoTracking()
            .Where(chunk =>
                chunk.DocumentId == documentId &&
                chunk.Document.ProjectId == projectId &&
                chunk.Document.Project.OwnerId == ownerId)
            .OrderBy(chunk => chunk.ChunkIndex)
            .Select(chunk => new DocumentChunkResponse(
                chunk.ChunkIndex,
                chunk.Content,
                chunk.TokenCount,
                chunk.CharacterCount,
                chunk.PageStart,
                chunk.PageEnd,
                chunk.SectionTitle,
                chunk.SourceSectionStartIndex,
                chunk.SourceSectionEndIndex))
            .ToListAsync(cancellationToken);

        return Ok(chunks);
    }

    [HttpPost("{documentId:guid}/chunks/rebuild")]
    public async Task<ActionResult<DocumentDetails>> RebuildChunks(
        Guid projectId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return AuthenticationError();
        }

        var document = await _dbContext.Documents
            .SingleOrDefaultAsync(
                item =>
                    item.Id == documentId &&
                    item.ProjectId == projectId &&
                    item.Project.OwnerId == ownerId,
                cancellationToken);

        if (document is null)
        {
            return ResourceNotFound();
        }

        if (document.Status == DocumentStatus.Processing)
        {
            return Conflict(new ApiErrorResponse(
                "Chunks cannot be rebuilt while the document is processing."));
        }

        if (document.Status != DocumentStatus.Ready)
        {
            return Conflict(new ApiErrorResponse(
                "Chunks can be rebuilt only after processing is complete."));
        }

        var hasSourceSections = await _dbContext.DocumentTextSections
            .AsNoTracking()
            .AnyAsync(
                section => section.DocumentId == documentId,
                cancellationToken);

        if (!hasSourceSections)
        {
            return Conflict(new ApiErrorResponse(
                "Extracted text is required before chunks can be rebuilt."));
        }

        if (!await TryClaimRebuildAsync(
                document,
                ownerId,
                cancellationToken))
        {
            return Conflict(new ApiErrorResponse(
                "The document is already being processed or rebuilt."));
        }

        try
        {
            var result = await _chunkingService.RebuildAsync(
                documentId,
                cancellationToken);

            _logger.LogInformation(
                "User requested chunk rebuild for document {DocumentId} in project {ProjectId}; generated {ChunkCount} chunks.",
                documentId,
                projectId,
                result.ChunkCount);

            var refreshedDocument = await _dbContext.Documents
                .AsNoTracking()
                .Include(item => item.Understanding)
                .Include(item => item.TechnicalAnalysis)
                .Include(item => item.OcrAnalysis)
                .SingleAsync(item => item.Id == documentId, cancellationToken);

            return Ok(ToDetails(refreshedDocument, embeddingsAreCurrent: true));
        }
        catch (DocumentEmbeddingException exception)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(exception.SafeMessage));
        }
        catch (DocumentChunkingException exception)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(exception.SafeMessage));
        }
    }

    [HttpPost("{documentId:guid}/embeddings/rebuild")]
    public async Task<ActionResult<DocumentDetails>> RebuildEmbeddings(
        Guid projectId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return AuthenticationError();
        }

        var document = await _dbContext.Documents
            .SingleOrDefaultAsync(
                item =>
                    item.Id == documentId &&
                    item.ProjectId == projectId &&
                    item.Project.OwnerId == ownerId,
                cancellationToken);

        if (document is null)
        {
            return ResourceNotFound();
        }

        if (document.Status == DocumentStatus.Processing)
        {
            return Conflict(new ApiErrorResponse(
                "Embeddings cannot be rebuilt while the document is processing."));
        }

        if (document.Status != DocumentStatus.Ready)
        {
            return Conflict(new ApiErrorResponse(
                "Document chunks must be ready before embeddings can be generated."));
        }

        var chunkCount = await _dbContext.DocumentChunks
            .AsNoTracking()
            .CountAsync(
                chunk =>
                    chunk.DocumentId == documentId &&
                    chunk.Document.ProjectId == projectId &&
                    chunk.Document.Project.OwnerId == ownerId,
                cancellationToken);

        if (chunkCount == 0)
        {
            return Conflict(new ApiErrorResponse(
                "Document chunks are required before embeddings can be generated."));
        }

        if (!await TryClaimRebuildAsync(
                document,
                ownerId,
                cancellationToken))
        {
            return Conflict(new ApiErrorResponse(
                "The document is already being processed or rebuilt."));
        }

        try
        {
            var result = await _embeddingService.RebuildAsync(
                documentId,
                cancellationToken);
            var refreshedDocument = await _dbContext.Documents
                .AsNoTracking()
                .Include(item => item.Understanding)
                .Include(item => item.TechnicalAnalysis)
                .Include(item => item.OcrAnalysis)
                .SingleAsync(item => item.Id == documentId, cancellationToken);

            _logger.LogInformation(
                "User-requested embedding rebuild completed for document {DocumentId} in project {ProjectId} with {ChunkCount} chunks using model {EmbeddingModel} with {EmbeddingDimensions} dimensions.",
                documentId,
                projectId,
                result.EmbeddedChunkCount,
                result.EmbeddingModel,
                result.EmbeddingDimensions);

            return Ok(ToDetails(refreshedDocument, embeddingsAreCurrent: true));
        }
        catch (DocumentEmbeddingException exception)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(exception.SafeMessage));
        }
    }

    [HttpDelete("{documentId:guid}")]
    public async Task<IActionResult> Delete(
        Guid projectId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return AuthenticationError();
        }

        var document = await _dbContext.Documents
            .SingleOrDefaultAsync(
                document =>
                    document.Id == documentId &&
                    document.ProjectId == projectId &&
                    document.Project.OwnerId == ownerId,
                cancellationToken);

        if (document is null)
        {
            return ResourceNotFound();
        }

        if (document.Status == DocumentStatus.Processing ||
            await _dbContext.DocumentUnderstandings
                .AsNoTracking()
                .AnyAsync(
                    understanding =>
                        understanding.DocumentId == documentId &&
                        understanding.Status == DocumentUnderstandingStatus.Processing,
                    cancellationToken))
        {
            return Conflict(new ApiErrorResponse(
                "The document cannot be deleted while it is processing or being rebuilt."));
        }

        if (await _dbContext.DocumentTechnicalAnalyses
                .AsNoTracking()
                .AnyAsync(
                    analysis =>
                        analysis.DocumentId == documentId &&
                        analysis.Status == DocumentTechnicalAnalysisStatus.Processing,
                    cancellationToken))
        {
            return Conflict(new ApiErrorResponse(
                "The document cannot be deleted while it is processing or being rebuilt."));
        }

        if (_dbContext.Database.IsRelational())
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);
            var claimed = await _dbContext.Documents
                .Where(item =>
                    item.Id == documentId &&
                    item.ProjectId == projectId &&
                    item.Project.OwnerId == ownerId &&
                    item.Status == document.Status &&
                    (item.Understanding == null ||
                     item.Understanding.Status != DocumentUnderstandingStatus.Processing) &&
                    (item.TechnicalAnalysis == null ||
                     item.TechnicalAnalysis.Status != DocumentTechnicalAnalysisStatus.Processing))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, DocumentStatus.Processing)
                    .SetProperty(item => item.UpdatedAtUtc, DateTime.UtcNow),
                    cancellationToken);

            if (claimed != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict(new ApiErrorResponse(
                    "The document cannot be deleted while it is processing or being rebuilt."));
            }

            if (await _fileStorage.ExistsAsync(document.StoredFileName, cancellationToken))
            {
                await _fileStorage.DeleteAsync(document.StoredFileName, cancellationToken);
            }

            var deleted = await _dbContext.Documents
                .Where(item =>
                    item.Id == documentId &&
                    item.ProjectId == projectId &&
                    item.Project.OwnerId == ownerId &&
                    item.Status == DocumentStatus.Processing)
                .ExecuteDeleteAsync(cancellationToken);

            if (deleted != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict(new ApiErrorResponse(
                    "The document could not be deleted because its state changed."));
            }

            await transaction.CommitAsync(cancellationToken);
            return NoContent();
        }

        if (await _fileStorage.ExistsAsync(document.StoredFileName, cancellationToken))
        {
            await _fileStorage.DeleteAsync(document.StoredFileName, cancellationToken);
        }

        _dbContext.Documents.Remove(document);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task<bool> TryClaimRebuildAsync(
        Document document,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var expectedStatus = document.Status;
        var now = DateTime.UtcNow;

        if (_dbContext.Database.IsRelational())
        {
            var updated = await _dbContext.Documents
                .Where(item =>
                    item.Id == document.Id &&
                    item.ProjectId == document.ProjectId &&
                    item.Project.OwnerId == ownerId &&
                    item.Status == expectedStatus &&
                    (item.Understanding == null ||
                     item.Understanding.Status != DocumentUnderstandingStatus.Processing) &&
                    (item.TechnicalAnalysis == null ||
                     item.TechnicalAnalysis.Status != DocumentTechnicalAnalysisStatus.Processing))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, DocumentStatus.Processing)
                    .SetProperty(item => item.ProcessingStartedAtUtc, now)
                    .SetProperty(item => item.ProcessingError, (string?)null)
                    .SetProperty(item => item.NormalizationError, (string?)null)
                    .SetProperty(item => item.ChunkingError, (string?)null)
                    .SetProperty(item => item.EmbeddingError, (string?)null)
                    .SetProperty(item => item.UpdatedAtUtc, now),
                    cancellationToken);

            _dbContext.ChangeTracker.Clear();
            return updated == 1;
        }

        if (document.Status != expectedStatus ||
            await _dbContext.DocumentUnderstandings
                .AsNoTracking()
                .AnyAsync(
                    understanding =>
                        understanding.DocumentId == document.Id &&
                    understanding.Status == DocumentUnderstandingStatus.Processing,
                    cancellationToken) ||
            await _dbContext.DocumentTechnicalAnalyses
                .AsNoTracking()
                .AnyAsync(
                    analysis =>
                        analysis.DocumentId == document.Id &&
                        analysis.Status == DocumentTechnicalAnalysisStatus.Processing,
                    cancellationToken))
        {
            return false;
        }

        document.Status = DocumentStatus.Processing;
        document.ProcessingStartedAtUtc = now;
        document.ProcessingError = null;
        document.NormalizationError = null;
        document.ChunkingError = null;
        document.EmbeddingError = null;
        document.UpdatedAtUtc = now;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<FileValidationResult> ValidateFileAsync(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return FileValidationResult.Invalid("Select a PDF or DOCX file.");
        }

        if (file.Length == 0)
        {
            return FileValidationResult.Invalid("The selected file is empty.");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return FileValidationResult.Invalid("The file cannot exceed 20 MB.");
        }

        var originalFileName = Path.GetFileName(file.FileName).Trim();

        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            return FileValidationResult.Invalid("The original filename is required.");
        }

        if (originalFileName.Length > 255)
        {
            return FileValidationResult.Invalid("The filename cannot exceed 255 characters.");
        }

        if (originalFileName.Any(char.IsControl))
        {
            return FileValidationResult.Invalid("The filename contains unsupported characters.");
        }

        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();

        if (!AllowedFileTypes.TryGetValue(extension, out var expectedContentType))
        {
            return FileValidationResult.Invalid("Only PDF and DOCX files are supported.");
        }

        if (!string.Equals(
                file.ContentType.Trim(),
                expectedContentType,
                StringComparison.OrdinalIgnoreCase))
        {
            return FileValidationResult.Invalid(
                $"The content type does not match the {extension.TrimStart('.').ToUpperInvariant()} file type.");
        }

        var hasValidSignature = extension == ".pdf"
            ? await IsPdfAsync(file, cancellationToken)
            : await IsDocxAsync(file, cancellationToken);

        if (!hasValidSignature)
        {
            return FileValidationResult.Invalid(
                "The file contents do not match the selected PDF or DOCX type.");
        }

        return FileValidationResult.Valid(
            new ValidatedFile(originalFileName, extension, expectedContentType));
    }

    private static async Task<bool> IsPdfAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var header = new byte[PdfSignature.Length];

        try
        {
            await stream.ReadExactlyAsync(header, cancellationToken);
            return header.SequenceEqual(PdfSignature);
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    private static async Task<bool> IsDocxAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var header = new byte[ZipSignature.Length];

        try
        {
            await stream.ReadExactlyAsync(header, cancellationToken);

            if (!header.SequenceEqual(ZipSignature))
            {
                return false;
            }

            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

            var entryNames = archive.Entries
                .Select(entry => entry.FullName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return entryNames.Contains("[Content_Types].xml") &&
                   entryNames.Contains("word/document.xml");
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    private bool TryGetOwnerId(out Guid ownerId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out ownerId);

    private UnauthorizedObjectResult AuthenticationError() =>
        Unauthorized(new ApiErrorResponse("Authentication is required."));

    private NotFoundObjectResult ResourceNotFound() =>
        NotFound(new ApiErrorResponse("Project or document not found."));

    private BadRequestObjectResult FileValidationError(string error) =>
        BadRequest(new ApiErrorResponse(
            "File validation failed.",
            new Dictionary<string, string[]> { ["file"] = [error] }));

    private async Task<IReadOnlyDictionary<Guid, bool>> LoadEmbeddingCurrencyAsync(
        IReadOnlyList<Document> documents,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var candidates = documents
            .Where(HasCurrentEmbeddingAggregate)
            .ToDictionary(document => document.Id);
        if (candidates.Count == 0)
        {
            return new Dictionary<Guid, bool>();
        }

        var candidateIds = candidates.Keys.ToArray();
        var chunkStates = await _dbContext.DocumentChunks
            .AsNoTracking()
            .Where(chunk =>
                candidateIds.Contains(chunk.DocumentId) &&
                chunk.Document.Project.OwnerId == ownerId)
            .Select(chunk => new ChunkEmbeddingCurrency(
                chunk.DocumentId,
                chunk.Content,
                chunk.Embedding != null,
                chunk.EmbeddingModel,
                chunk.EmbeddingDimensions,
                chunk.EmbeddingContentHash,
                chunk.EmbeddedAtUtc))
            .ToListAsync(cancellationToken);

        var chunksByDocument = chunkStates
            .GroupBy(chunk => chunk.DocumentId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var result = new Dictionary<Guid, bool>(candidates.Count);

        foreach (var (documentId, document) in candidates)
        {
            result[documentId] = chunksByDocument.TryGetValue(documentId, out var chunks) &&
                chunks.Length == document.ChunkCount &&
                chunks.All(chunk =>
                    chunk.HasEmbedding &&
                    string.Equals(
                        chunk.EmbeddingModel,
                        _embeddingOptions.EmbeddingModel,
                        StringComparison.Ordinal) &&
                    chunk.EmbeddingDimensions == _embeddingOptions.EmbeddingDimensions &&
                    chunk.EmbeddedAtUtc == document.EmbeddedAtUtc &&
                    string.Equals(
                        chunk.EmbeddingContentHash,
                        EmbeddingContentHasher.Compute(chunk.Content),
                        StringComparison.Ordinal));
        }

        return result;
    }

    private bool HasCurrentEmbeddingAggregate(Document document) =>
        document.Status == DocumentStatus.Ready &&
        document.ChunkCount > 0 &&
        document.EmbeddedChunkCount == document.ChunkCount &&
        string.Equals(
            document.EmbeddingModel,
            _embeddingOptions.EmbeddingModel,
            StringComparison.Ordinal) &&
        document.EmbeddingDimensions == _embeddingOptions.EmbeddingDimensions &&
        document.EmbeddedAtUtc is not null;

    private static DocumentSummary ToSummary(
        Document document,
        bool embeddingsAreCurrent) =>
        new(
            document.Id,
            document.OriginalFileName,
            document.ContentType,
            document.FileSizeBytes,
            document.Status,
            document.CreatedAtUtc,
            document.UpdatedAtUtc,
            document.ProcessingStartedAtUtc,
            document.ProcessedAtUtc,
            document.ExtractedSectionCount,
            document.ExtractedCharacterCount,
            document.ProcessingError,
            document.ChunkCount,
            document.ChunkedAtUtc,
            document.ChunkingError,
            document.NormalizedCharacterCount,
            document.NormalizationRemovedCharacterCount,
            document.NormalizationChangedSectionCount,
            document.NormalizedAtUtc,
            document.NormalizationError,
            document.EmbeddedChunkCount,
            document.EmbeddingModel,
            document.EmbeddingDimensions,
            document.EmbeddedAtUtc,
            document.EmbeddingError,
            embeddingsAreCurrent,
            document.Understanding?.Status,
            document.TechnicalAnalysis?.Status,
            document.OcrAnalysis?.Status);

    private DocumentDetails ToDetails(
        Document document,
        bool embeddingsAreCurrent = false) =>
        new(
            document.Id,
            document.ProjectId,
            document.OriginalFileName,
            document.ContentType,
            document.FileSizeBytes,
            document.Status,
            document.CreatedAtUtc,
            document.UpdatedAtUtc,
            document.ProcessingStartedAtUtc,
            document.ProcessedAtUtc,
            document.ExtractedSectionCount,
            document.ExtractedCharacterCount,
            document.ProcessingError,
            document.ChunkCount,
            document.ChunkedAtUtc,
            document.ChunkingError,
            document.NormalizedCharacterCount,
            document.NormalizationRemovedCharacterCount,
            document.NormalizationChangedSectionCount,
            document.NormalizedAtUtc,
            document.NormalizationError,
            document.EmbeddedChunkCount,
            document.EmbeddingModel,
            document.EmbeddingDimensions,
            document.EmbeddedAtUtc,
            document.EmbeddingError,
            embeddingsAreCurrent,
            document.Understanding?.Status,
            document.TechnicalAnalysis?.Status,
            document.OcrAnalysis?.Status);

    private sealed record ValidatedFile(
        string OriginalFileName,
        string Extension,
        string ContentType);

    private sealed record FileValidationResult(ValidatedFile? File, string? Error)
    {
        public static FileValidationResult Valid(ValidatedFile file) => new(file, null);

        public static FileValidationResult Invalid(string error) => new(null, error);
    }

    private sealed record ChunkEmbeddingCurrency(
        Guid DocumentId,
        string Content,
        bool HasEmbedding,
        string? EmbeddingModel,
        int? EmbeddingDimensions,
        string? EmbeddingContentHash,
        DateTime? EmbeddedAtUtc);
}
