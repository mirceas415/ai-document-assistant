namespace AI.DocumentAssistant.Server.Embeddings;

public static class EmbeddingResultValidator
{
    public static void Validate(
        TextEmbeddingResult result,
        int expectedCount,
        string expectedModel,
        int expectedDimensions)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!string.Equals(result.Model, expectedModel, StringComparison.Ordinal))
        {
            throw new DocumentEmbeddingException(
                "The embedding service returned an unexpected model. Please retry.");
        }

        if (result.Dimensions != expectedDimensions)
        {
            throw new DocumentEmbeddingException(
                "The embedding service returned an unexpected vector size. Please retry.");
        }

        if (result.Embeddings.Count != expectedCount)
        {
            throw new DocumentEmbeddingException(
                "The embedding service returned an unexpected result count. Please retry.");
        }

        for (var index = 0; index < result.Embeddings.Count; index++)
        {
            var vector = result.Embeddings[index];
            if (vector is null || vector.Length != expectedDimensions)
            {
                throw new DocumentEmbeddingException(
                    "The embedding service returned an unexpected vector size. Please retry.");
            }

            if (vector.Any(value => !float.IsFinite(value)))
            {
                throw new DocumentEmbeddingException(
                    "The embedding service returned invalid vector values. Please retry.");
            }
        }
    }
}
