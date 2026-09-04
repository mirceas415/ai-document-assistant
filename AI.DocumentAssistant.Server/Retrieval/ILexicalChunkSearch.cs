namespace AI.DocumentAssistant.Server.Retrieval;

public interface ILexicalChunkSearch
{
    Task<IReadOnlyList<RetrievedDocumentChunk>> SearchAsync(
        Guid ownerId,
        Guid projectId,
        RetrievalQuery query,
        int candidateCount,
        CancellationToken cancellationToken);
}
