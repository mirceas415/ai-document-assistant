using System.IO.Compression;
using System.Security.Claims;
using AI.DocumentAssistant.Server.Contracts;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        ApplicationDbContext dbContext,
        IFileStorageService fileStorage,
        ILogger<DocumentsController> logger)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
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
            .Where(document =>
                document.ProjectId == projectId &&
                document.Project.OwnerId == ownerId)
            .OrderByDescending(document => document.CreatedAtUtc)
            .Select(document => new DocumentSummary(
                document.Id,
                document.OriginalFileName,
                document.ContentType,
                document.FileSizeBytes,
                document.Status,
                document.CreatedAtUtc,
                document.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(documents);
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
            .Where(document =>
                document.Id == documentId &&
                document.ProjectId == projectId &&
                document.Project.OwnerId == ownerId)
            .Select(document => new DocumentDetails(
                document.Id,
                document.ProjectId,
                document.OriginalFileName,
                document.ContentType,
                document.FileSizeBytes,
                document.Status,
                document.CreatedAtUtc,
                document.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

        return document is null
            ? ResourceNotFound()
            : Ok(document);
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

            _dbContext.Documents.Add(document);
            await _dbContext.SaveChangesAsync(cancellationToken);

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

        if (await _fileStorage.ExistsAsync(document.StoredFileName, cancellationToken))
        {
            await _fileStorage.DeleteAsync(document.StoredFileName, cancellationToken);
        }

        _dbContext.Documents.Remove(document);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
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

    private static DocumentDetails ToDetails(Document document) =>
        new(
            document.Id,
            document.ProjectId,
            document.OriginalFileName,
            document.ContentType,
            document.FileSizeBytes,
            document.Status,
            document.CreatedAtUtc,
            document.UpdatedAtUtc);

    private sealed record ValidatedFile(
        string OriginalFileName,
        string Extension,
        string ContentType);

    private sealed record FileValidationResult(ValidatedFile? File, string? Error)
    {
        public static FileValidationResult Valid(ValidatedFile file) => new(file, null);

        public static FileValidationResult Invalid(string error) => new(null, error);
    }
}
