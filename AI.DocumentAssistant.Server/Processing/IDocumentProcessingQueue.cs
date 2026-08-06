namespace AI.DocumentAssistant.Server.Processing;

public interface IDocumentProcessingQueue
{
    bool TryEnqueue(Guid documentId);

    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken);

    void Complete(Guid documentId);
}
