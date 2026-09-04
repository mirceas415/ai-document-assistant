namespace AI.DocumentAssistant.Server.Processing;

public interface IDocumentProcessingService
{
    Task ProcessAsync(Guid documentId, CancellationToken cancellationToken);

    Task RebuildOcrAsync(Guid documentId, CancellationToken cancellationToken);
}
