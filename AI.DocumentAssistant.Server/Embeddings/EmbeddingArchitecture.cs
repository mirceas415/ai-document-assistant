namespace AI.DocumentAssistant.Server.Embeddings;

/// <summary>
/// Persistence-level embedding constraints. Changing the dimension requires an EF migration
/// because PostgreSQL stores chunk embeddings in a fixed vector(1536) column.
/// </summary>
public static class EmbeddingArchitecture
{
    public const string DefaultModel = "text-embedding-3-small";

    public const int Dimensions = 1536;

    public const int DefaultBatchSize = 32;

    public const int MaximumBatchSize = 128;

    public const int MaximumModelNameLength = 100;

    public const int ContentHashLength = 64;
}
