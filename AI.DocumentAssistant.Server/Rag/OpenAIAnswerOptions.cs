using System.ComponentModel.DataAnnotations;

namespace AI.DocumentAssistant.Server.Rag;

public sealed class OpenAIAnswerOptions
{
    public const string SectionName = "OpenAI";

    [Required]
    [MaxLength(100)]
    public string AnswerModel { get; init; } = RagArchitecture.DefaultAnswerModel;

    [Range(1, Retrieval.SemanticRetrievalLimits.MaximumTopK)]
    public int AnswerRetrievalTopK { get; init; } =
        Retrieval.SemanticRetrievalLimits.DefaultTopK;

    [Range(RagArchitecture.MinimumContextTokens, RagArchitecture.MaximumContextTokens)]
    public int MaxContextTokens { get; init; } = RagArchitecture.DefaultContextTokens;

    [Range(1, RagArchitecture.MaximumAnswerTokens)]
    public int MaxAnswerTokens { get; init; } = RagArchitecture.DefaultAnswerTokens;

    [Range(1, RagArchitecture.MaximumSourceExcerptCharacters)]
    public int SourceExcerptCharacters { get; init; } =
        RagArchitecture.DefaultSourceExcerptCharacters;
}
