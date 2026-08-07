namespace AI.DocumentAssistant.Server.Retrieval;

public sealed class SemanticRetrievalException : Exception
{
    public SemanticRetrievalException(string safeMessage)
        : base(safeMessage)
    {
        SafeMessage = safeMessage;
    }

    public SemanticRetrievalException(string safeMessage, Exception innerException)
        : base(safeMessage, innerException)
    {
        SafeMessage = safeMessage;
    }

    public string SafeMessage { get; }
}
