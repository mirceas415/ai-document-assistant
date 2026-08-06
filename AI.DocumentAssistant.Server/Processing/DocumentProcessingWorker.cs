namespace AI.DocumentAssistant.Server.Processing;

public sealed class DocumentProcessingWorker : BackgroundService
{
    private readonly IDocumentProcessingQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentProcessingWorker> _logger;

    public DocumentProcessingWorker(
        IDocumentProcessingQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<DocumentProcessingWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var documentId in _queue.ReadAllAsync(stoppingToken))
            {
                _logger.LogInformation(
                    "Starting queued processing job for document {DocumentId}.",
                    documentId);

                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var processingService = scope.ServiceProvider
                        .GetRequiredService<IDocumentProcessingService>();

                    await processingService.ProcessAsync(documentId, stoppingToken);

                    _logger.LogInformation(
                        "Finished queued processing job for document {DocumentId}.",
                        documentId);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation(
                        "Processing job for document {DocumentId} was cancelled during shutdown.",
                        documentId);
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "Queued processing job failed for document {DocumentId}.",
                        documentId);
                }
                finally
                {
                    _queue.Complete(documentId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Document processing worker stopped.");
        }
    }
}
