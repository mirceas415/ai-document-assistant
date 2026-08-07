using AI.DocumentAssistant.Server.Retrieval;

namespace AI.DocumentAssistant.Server.Rag;

public interface IRagContextBuilder
{
    RagContext Build(IReadOnlyList<RetrievedDocumentChunk> chunks);
}

public sealed record RagContext(
    string Text,
    int ApproximateTokenCount,
    IReadOnlyList<RagSource> Sources);

public sealed record RagSource(
    string SourceId,
    RetrievedDocumentChunk Chunk);
