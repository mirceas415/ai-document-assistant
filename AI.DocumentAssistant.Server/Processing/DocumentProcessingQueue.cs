using System.Collections.Concurrent;
using System.Threading.Channels;

namespace AI.DocumentAssistant.Server.Processing;

/// <summary>
/// A bounded, process-local queue. Pending items are not durable and can be lost when the
/// application stops, so Uploaded and Failed documents can be manually queued again.
/// </summary>
public sealed class DocumentProcessingQueue : IDocumentProcessingQueue
{
    private const int Capacity = 100;

    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    private readonly ConcurrentDictionary<Guid, byte> _scheduledDocuments = new();

    public bool TryEnqueue(Guid documentId)
    {
        if (!_scheduledDocuments.TryAdd(documentId, 0))
        {
            return true;
        }

        if (_channel.Writer.TryWrite(documentId))
        {
            return true;
        }

        _scheduledDocuments.TryRemove(documentId, out _);
        return false;
    }

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void Complete(Guid documentId) =>
        _scheduledDocuments.TryRemove(documentId, out _);
}
