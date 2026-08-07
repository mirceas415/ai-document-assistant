namespace AI.DocumentAssistant.Server.Contracts;

public sealed class SemanticSearchRequest
{
    public string? Query { get; init; }

    public int? TopK { get; init; }
}

public sealed record SemanticSearchResponse(
    int TopK,
    IReadOnlyList<SemanticSearchResultResponse> Results);

public sealed record SemanticSearchResultResponse(
    Guid DocumentId,
    string DocumentName,
    Guid ChunkId,
    int ChunkIndex,
    string Content,
    int? PageStart,
    int? PageEnd,
    string? Heading,
    double CosineDistance);
