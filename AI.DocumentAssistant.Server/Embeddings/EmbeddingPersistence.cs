using AI.DocumentAssistant.Server.Models;
using Pgvector;

namespace AI.DocumentAssistant.Server.Embeddings;

public static class EmbeddingPersistence
{
    public static void ApplyToChunk(
        DocumentChunk chunk,
        float[] embedding,
        TextEmbeddingResult result,
        DateTime embeddedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(embedding);
        ArgumentNullException.ThrowIfNull(result);

        if (embedding.Length != EmbeddingArchitecture.Dimensions ||
            result.Dimensions != EmbeddingArchitecture.Dimensions)
        {
            throw new DocumentEmbeddingException(
                "The embedding service returned an unexpected vector size. Please retry.");
        }

        chunk.Embedding = new Vector(embedding);
        chunk.EmbeddingModel = result.Model;
        chunk.EmbeddingDimensions = result.Dimensions;
        chunk.EmbeddingContentHash = EmbeddingContentHasher.Compute(chunk.Content);
        chunk.EmbeddedAtUtc = embeddedAtUtc;
    }

    public static void ApplyToDocument(
        Document document,
        int embeddedChunkCount,
        TextEmbeddingResult result,
        DateTime embeddedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(result);

        document.EmbeddedChunkCount = embeddedChunkCount;
        document.EmbeddingModel = result.Model;
        document.EmbeddingDimensions = result.Dimensions;
        document.EmbeddedAtUtc = embeddedAtUtc;
        document.EmbeddingError = null;
    }

    public static void ClearDocumentMetadata(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.EmbeddedChunkCount = 0;
        document.EmbeddingModel = null;
        document.EmbeddingDimensions = null;
        document.EmbeddedAtUtc = null;
        document.EmbeddingError = null;
    }
}
