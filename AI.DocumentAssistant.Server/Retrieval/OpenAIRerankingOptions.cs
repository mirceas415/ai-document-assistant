using System.ComponentModel.DataAnnotations;

namespace AI.DocumentAssistant.Server.Retrieval;

public sealed class OpenAIRerankingOptions
{
    public const string SectionName = "OpenAI";

    [MaxLength(100)]
    public string? RerankingModel { get; init; }
}
