namespace AI.DocumentAssistant.Server.Embeddings;

public sealed class DocumentEmbeddingException : Exception
{
    public DocumentEmbeddingException(string safeMessage)
        : base(safeMessage)
    {
        SafeMessage = safeMessage;
    }

    public DocumentEmbeddingException(string safeMessage, Exception innerException)
        : base(safeMessage, innerException)
    {
        SafeMessage = safeMessage;
    }

    public string SafeMessage { get; }
}
