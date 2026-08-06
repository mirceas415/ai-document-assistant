namespace AI.DocumentAssistant.Server.Chunking;

public sealed class DocumentChunkingException : Exception
{
    public DocumentChunkingException(string safeMessage)
        : base(safeMessage)
    {
        SafeMessage = safeMessage;
    }

    public DocumentChunkingException(string safeMessage, Exception innerException)
        : base(safeMessage, innerException)
    {
        SafeMessage = safeMessage;
    }

    public string SafeMessage { get; }
}
