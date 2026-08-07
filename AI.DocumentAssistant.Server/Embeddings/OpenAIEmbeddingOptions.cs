using System.ComponentModel.DataAnnotations;

namespace AI.DocumentAssistant.Server.Embeddings;

public sealed class OpenAIEmbeddingOptions
{
    public const string SectionName = "OpenAI";

    [Required]
    [MaxLength(EmbeddingArchitecture.MaximumModelNameLength)]
    public string EmbeddingModel { get; init; } = EmbeddingArchitecture.DefaultModel;

    [Range(1, int.MaxValue)]
    public int EmbeddingDimensions { get; init; } = EmbeddingArchitecture.Dimensions;

    [Range(1, EmbeddingArchitecture.MaximumBatchSize)]
    public int BatchSize { get; init; } = EmbeddingArchitecture.DefaultBatchSize;
}
