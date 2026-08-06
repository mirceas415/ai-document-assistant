namespace AI.DocumentAssistant.Server.Normalization;

public interface IDocumentNormalizationService
{
    Task<DocumentNormalizationRebuildResult> RebuildAsync(
        Guid documentId,
        CancellationToken cancellationToken);
}

public sealed record DocumentNormalizationRebuildResult(
    int ChangedSectionCount,
    long RemovedCharacterCount,
    long NormalizedCharacterCount,
    int ChunkCount,
    DateTime NormalizedAtUtc);
