namespace AI.DocumentAssistant.Server.Contracts;

public sealed class CreateProjectRequest
{
    public string? Name { get; init; }

    public string? Description { get; init; }
}

public sealed class UpdateProjectRequest
{
    public string? Name { get; init; }

    public string? Description { get; init; }
}

public sealed record ProjectSummaryResponse(
    Guid Id,
    string Name,
    string? Description,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record ProjectDetailsResponse(
    Guid Id,
    string Name,
    string? Description,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
