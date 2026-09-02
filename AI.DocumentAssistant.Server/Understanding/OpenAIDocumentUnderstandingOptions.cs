using System.ComponentModel.DataAnnotations;

namespace AI.DocumentAssistant.Server.Understanding;

public sealed class OpenAIDocumentUnderstandingOptions
{
    public const string SectionName = "OpenAI";

    [MaxLength(DocumentUnderstandingLimits.MaximumModelLength)]
    public string? DocumentUnderstandingModel { get; init; }
}
