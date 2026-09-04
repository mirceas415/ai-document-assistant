namespace AI.DocumentAssistant.Server.Retrieval;

public interface IHybridRetrievalFusion
{
    IReadOnlyList<RetrievedDocumentChunk> Fuse(
        IReadOnlyList<RetrievedDocumentChunk> vectorCandidates,
        IReadOnlyList<RetrievedDocumentChunk> lexicalCandidates,
        IReadOnlyList<MetadataDocumentMatch> metadataDocuments,
        int candidateCount);
}
