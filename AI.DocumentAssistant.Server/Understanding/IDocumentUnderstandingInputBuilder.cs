namespace AI.DocumentAssistant.Server.Understanding;

public interface IDocumentUnderstandingInputBuilder
{
    DocumentUnderstandingInput Build(
        IReadOnlyList<DocumentUnderstandingSourceSection> sourceSections,
        CancellationToken cancellationToken = default);
}

public sealed record DocumentUnderstandingInput(
    string Content,
    string SourceContentHash,
    int FullTokenCount,
    int InputTokenCount,
    bool IsSampled,
    bool HasSufficientText,
    string? SkipReason);
