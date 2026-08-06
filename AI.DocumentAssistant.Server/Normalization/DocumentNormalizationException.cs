namespace AI.DocumentAssistant.Server.Normalization;

public sealed class DocumentNormalizationException : Exception
{
    public DocumentNormalizationException(string safeMessage)
        : base(safeMessage)
    {
        SafeMessage = safeMessage;
    }

    public DocumentNormalizationException(string safeMessage, Exception innerException)
        : base(safeMessage, innerException)
    {
        SafeMessage = safeMessage;
    }

    public string SafeMessage { get; }
}
