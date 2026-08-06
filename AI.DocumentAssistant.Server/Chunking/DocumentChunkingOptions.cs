using System.ComponentModel.DataAnnotations;

namespace AI.DocumentAssistant.Server.Chunking;

public sealed class DocumentChunkingOptions
{
    public const string SectionName = "Chunking";

    [Range(1, int.MaxValue)]
    public int TargetTokens { get; init; } = 700;

    [Range(1, int.MaxValue)]
    public int MaximumTokens { get; init; } = 900;

    [Range(0, int.MaxValue)]
    public int OverlapTokens { get; init; } = 100;

    [Range(1, int.MaxValue)]
    public int MinimumTokens { get; init; } = 100;
}
