using AI.DocumentAssistant.Server.Models;

namespace AI.DocumentAssistant.Server.Understanding;

public interface IDocumentUnderstandingService
{
    Task<DocumentUnderstandingRunResult> AnalyzeAsync(
        Guid documentId,
        IReadOnlyList<DocumentUnderstandingSourceSection> sourceSections,
        bool force,
        CancellationToken cancellationToken);

    Task<DocumentUnderstandingRunResult> AnalyzePersistedAsync(
        Guid documentId,
        bool force,
        CancellationToken cancellationToken);

    Task StagePendingIfStaleAsync(
        Guid documentId,
        IReadOnlyList<DocumentUnderstandingSourceSection> sourceSections,
        CancellationToken cancellationToken);
}

public sealed record DocumentUnderstandingRunResult(
    DocumentUnderstandingStatus Status,
    string SourceContentHash,
    string Model,
    string PromptVersion,
    bool Reused);
