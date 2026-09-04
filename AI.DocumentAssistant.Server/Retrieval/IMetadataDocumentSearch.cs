namespace AI.DocumentAssistant.Server.Retrieval;

public interface IMetadataDocumentSearch
{
    Task<IReadOnlyList<MetadataDocumentMatch>> SearchAsync(
        Guid ownerId,
        Guid projectId,
        RetrievalQuery query,
        int candidateCount,
        CancellationToken cancellationToken);
}

public sealed record MetadataDocumentMatch(
    Guid DocumentId,
    int Rank,
    double MatchScore,
    bool HasExactIdentifierMatch,
    IReadOnlyList<MatchedRetrievalMetadata> MatchedMetadata);
