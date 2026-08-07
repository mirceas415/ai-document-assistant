namespace AI.DocumentAssistant.Server.Contracts;

public sealed class AskProjectRequest
{
    public string? Question { get; init; }
}

public sealed record AskProjectResponse(
    string Answer,
    IReadOnlyList<AskProjectSourceResponse> Sources);

public sealed record AskProjectSourceResponse(
    string SourceId,
    Guid DocumentId,
    string DocumentName,
    Guid ChunkId,
    int ChunkIndex,
    int? PageStart,
    int? PageEnd,
    string? Heading,
    string Excerpt);
