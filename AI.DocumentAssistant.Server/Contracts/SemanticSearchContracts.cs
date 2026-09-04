namespace AI.DocumentAssistant.Server.Contracts;

public sealed class SemanticSearchRequest
{
    public string? Query { get; init; }

    public int? TopK { get; init; }
}

public sealed record SemanticSearchResponse(
    int TopK,
    IReadOnlyList<SemanticSearchResultResponse> Results,
    bool RerankingApplied = false,
    bool RerankingFallback = false);

public sealed record SemanticSearchResultResponse(
    Guid DocumentId,
    string DocumentName,
    Guid ChunkId,
    int ChunkIndex,
    string Content,
    int? PageStart,
    int? PageEnd,
    string? Heading,
    double? CosineDistance,
    int? VectorRank = null,
    int? LexicalRank = null,
    int? MetadataDocumentRank = null,
    double? LexicalRankScore = null,
    double? FusedScore = null,
    IReadOnlyList<MatchedMetadataResponse>? MatchedMetadata = null,
    int? HybridRank = null,
    int? RerankRank = null,
    int? RerankRelevance = null);

public sealed record MatchedMetadataResponse(
    string Field,
    string Value,
    bool IsExact);
