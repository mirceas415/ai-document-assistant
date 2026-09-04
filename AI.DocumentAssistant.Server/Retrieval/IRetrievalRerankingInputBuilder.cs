namespace AI.DocumentAssistant.Server.Retrieval;

public interface IRetrievalRerankingInputBuilder
{
    RetrievalRerankingRequest Build(
        string question,
        IReadOnlyList<RetrievedDocumentChunk> hybridCandidates);
}
