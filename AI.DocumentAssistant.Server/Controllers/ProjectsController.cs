using System.Security.Claims;
using AI.DocumentAssistant.Server.Contracts;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Models;
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

    public ProjectsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
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

        var deletedCount = await _dbContext.Projects
            .Where(project => project.Id == id && project.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);

        return deletedCount == 0
            ? ProjectNotFound()
            : NoContent();
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
