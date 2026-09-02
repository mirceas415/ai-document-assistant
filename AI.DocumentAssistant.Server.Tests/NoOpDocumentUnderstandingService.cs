using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Understanding;

namespace AI.DocumentAssistant.Server.Tests;

internal sealed class NoOpDocumentUnderstandingService : IDocumentUnderstandingService
{
    public Task<DocumentUnderstandingRunResult> AnalyzeAsync(
        Guid documentId,
        IReadOnlyList<DocumentUnderstandingSourceSection> sourceSections,
        bool force,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var content = string.Join(
            "\n\n",
            sourceSections.OrderBy(section => section.SectionIndex)
                .Select(section => section.NormalizedContent));
        return Task.FromResult(new DocumentUnderstandingRunResult(
            DocumentUnderstandingStatus.Ready,
            DocumentUnderstandingContentHasher.Compute(content),
            "test-understanding-model",
            DocumentUnderstandingArchitecture.PromptVersion,
            false));
    }

    public Task<DocumentUnderstandingRunResult> AnalyzePersistedAsync(
        Guid documentId,
        bool force,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DocumentUnderstandingRunResult(
            DocumentUnderstandingStatus.Ready,
            DocumentUnderstandingContentHasher.Compute(string.Empty),
            "test-understanding-model",
            DocumentUnderstandingArchitecture.PromptVersion,
            false));
    }

    public Task StagePendingIfStaleAsync(
        Guid documentId,
        IReadOnlyList<DocumentUnderstandingSourceSection> sourceSections,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
