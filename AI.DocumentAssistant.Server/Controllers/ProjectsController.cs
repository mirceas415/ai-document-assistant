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
[Route("api/projects")]
public sealed class ProjectsController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IFileStorageService _fileStorage;

    public ProjectsController(
        ApplicationDbContext dbContext,
        IFileStorageService fileStorage)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProjectSummaryResponse>>> GetAll(
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return AuthenticationError();
        }

        var projects = await _dbContext.Projects
            .AsNoTracking()
            .Where(project => project.OwnerId == ownerId)
            .OrderByDescending(project => project.UpdatedAtUtc)
            .Select(project => new ProjectSummaryResponse(
                project.Id,
                project.Name,
                project.Description,
                project.CreatedAtUtc,
                project.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(projects);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProjectDetailsResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return AuthenticationError();
        }

        var project = await _dbContext.Projects
            .AsNoTracking()
            .Where(project => project.Id == id && project.OwnerId == ownerId)
            .Select(project => new ProjectDetailsResponse(
                project.Id,
                project.Name,
                project.Description,
                project.CreatedAtUtc,
                project.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

        return project is null
            ? ProjectNotFound()
            : Ok(project);
    }

    [HttpPost]
    public async Task<ActionResult<ProjectDetailsResponse>> Create(
        CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return AuthenticationError();
        }

        var validationError = ValidateAndNormalize(
            request.Name,
            request.Description,
            out var name,
            out var description);

        if (validationError is not null)
        {
            return validationError;
        }

        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            OwnerId = ownerId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = ToDetails(project);
        return CreatedAtAction(nameof(GetById), new { id = project.Id }, response);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProjectDetailsResponse>> Update(
        Guid id,
        UpdateProjectRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return AuthenticationError();
        }

        var validationError = ValidateAndNormalize(
            request.Name,
            request.Description,
            out var name,
            out var description);

        if (validationError is not null)
        {
            return validationError;
        }

        var project = await _dbContext.Projects
            .SingleOrDefaultAsync(
                project => project.Id == id && project.OwnerId == ownerId,
                cancellationToken);

        if (project is null)
        {
            return ProjectNotFound();
        }

        project.Name = name;
        project.Description = description;
        project.UpdatedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToDetails(project));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetOwnerId(out var ownerId))
        {
            return AuthenticationError();
        }

        var ownsProject = await _dbContext.Projects
            .AsNoTracking()
            .AnyAsync(
                project => project.Id == id && project.OwnerId == ownerId,
                cancellationToken);

        if (!ownsProject)
        {
            return ProjectNotFound();
        }

        if (_dbContext.Database.IsRelational())
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                cancellationToken);
            // FOR UPDATE also conflicts with the key-share lock needed by a concurrent
            // document insert, so an upload cannot appear after the file snapshot.
            var lockedProjects = await _dbContext.Projects
                .FromSqlInterpolated($$"""
                    SELECT *
                    FROM "Projects"
                    WHERE "Id" = {{id}} AND "OwnerId" = {{ownerId}}
                    FOR UPDATE
                    """)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (lockedProjects.Count != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ProjectNotFound();
            }

            var documents = await _dbContext.Documents
                .AsNoTracking()
                .Where(document =>
                    document.ProjectId == id &&
                    document.Project.OwnerId == ownerId)
                .Select(document => new
                {
                    document.Id,
                    document.StoredFileName,
                    document.Status
                })
                .ToListAsync(cancellationToken);

            if (documents.Any(document => document.Status == DocumentStatus.Processing))
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict(new ApiErrorResponse(
                    "The project cannot be deleted while one of its documents is processing or being rebuilt."));
            }

            var claimedDocuments = await _dbContext.Documents
                .Where(document =>
                    document.ProjectId == id &&
                    document.Project.OwnerId == ownerId &&
                    document.Status != DocumentStatus.Processing)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(document => document.Status, DocumentStatus.Processing)
                    .SetProperty(document => document.UpdatedAtUtc, DateTime.UtcNow),
                    cancellationToken);

            if (claimedDocuments != documents.Count)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict(new ApiErrorResponse(
                    "The project cannot be deleted while one of its documents is processing or being rebuilt."));
            }

            foreach (var document in documents)
            {
                if (await _fileStorage.ExistsAsync(document.StoredFileName, cancellationToken))
                {
                    await _fileStorage.DeleteAsync(document.StoredFileName, cancellationToken);
                }
            }

            var deletedProjects = await _dbContext.Projects
                .Where(project => project.Id == id && project.OwnerId == ownerId)
                .ExecuteDeleteAsync(cancellationToken);
            if (deletedProjects != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ProjectNotFound();
            }

            await transaction.CommitAsync(cancellationToken);
            return NoContent();
        }

        var hasProcessingDocument = await _dbContext.Documents
            .AsNoTracking()
            .AnyAsync(
                document =>
                    document.ProjectId == id &&
                    document.Project.OwnerId == ownerId &&
                    document.Status == DocumentStatus.Processing,
                cancellationToken);
        if (hasProcessingDocument)
        {
            return Conflict(new ApiErrorResponse(
                "The project cannot be deleted while one of its documents is processing or being rebuilt."));
        }

        var storedFileNames = await _dbContext.Documents
            .AsNoTracking()
            .Where(document =>
                document.ProjectId == id &&
                document.Project.OwnerId == ownerId)
            .Select(document => document.StoredFileName)
            .ToListAsync(cancellationToken);

        foreach (var storedFileName in storedFileNames)
        {
            if (await _fileStorage.ExistsAsync(storedFileName, cancellationToken))
            {
                await _fileStorage.DeleteAsync(storedFileName, cancellationToken);
            }
        }

        await _dbContext.Projects
            .Where(project => project.Id == id && project.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);

        return NoContent();
    }

    private bool TryGetOwnerId(out Guid ownerId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out ownerId);

    private UnauthorizedObjectResult AuthenticationError() =>
        Unauthorized(new ApiErrorResponse("Authentication is required."));

    private NotFoundObjectResult ProjectNotFound() =>
        NotFound(new ApiErrorResponse("Project not found."));

    private BadRequestObjectResult? ValidateAndNormalize(
        string? requestedName,
        string? requestedDescription,
        out string name,
        out string? description)
    {
        name = requestedName?.Trim() ?? string.Empty;
        description = string.IsNullOrWhiteSpace(requestedDescription)
            ? null
            : requestedDescription.Trim();

        var errors = new Dictionary<string, string[]>();

        if (name.Length == 0)
        {
            errors["name"] = ["Project name is required."];
        }
        else if (name.Length > 100)
        {
            errors["name"] = ["Project name cannot exceed 100 characters."];
        }

        if (description?.Length > 1_000)
        {
            errors["description"] = ["Description cannot exceed 1,000 characters."];
        }

        return errors.Count == 0
            ? null
            : BadRequest(new ApiErrorResponse("Validation failed.", errors));
    }

    private static ProjectDetailsResponse ToDetails(Project project) =>
        new(
            project.Id,
            project.Name,
            project.Description,
            project.CreatedAtUtc,
            project.UpdatedAtUtc);
}
