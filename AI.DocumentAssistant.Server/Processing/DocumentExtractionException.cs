namespace AI.DocumentAssistant.Server.Processing;

public sealed class DocumentExtractionException : Exception
{
    public DocumentExtractionException(string safeMessage)
        : base(safeMessage)
    {
        SafeMessage = safeMessage;
    }

    public DocumentExtractionException(string safeMessage, Exception innerException)
        : base(safeMessage, innerException)
    {
        SafeMessage = safeMessage;
    }

    public string SafeMessage { get; }
}
