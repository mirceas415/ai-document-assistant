namespace AI.DocumentAssistant.Server.Chunking;

public interface IDocumentChunkGenerator
{
    IReadOnlyList<GeneratedDocumentChunk> Generate(
        IReadOnlyList<ChunkSourceSection> sourceSections,
        CancellationToken cancellationToken = default);
}
