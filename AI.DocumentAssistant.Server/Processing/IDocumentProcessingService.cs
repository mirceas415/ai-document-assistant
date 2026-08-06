namespace AI.DocumentAssistant.Server.Processing;

public interface IDocumentProcessingService
{
    Task ProcessAsync(Guid documentId, CancellationToken cancellationToken);
}
