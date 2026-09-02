namespace AI.DocumentAssistant.Server.Understanding;

public class DocumentUnderstandingException : Exception
{
    public DocumentUnderstandingException(string safeMessage)
        : base(safeMessage)
    {
        SafeMessage = safeMessage;
    }

    public DocumentUnderstandingException(string safeMessage, Exception innerException)
        : base(safeMessage, innerException)
    {
        SafeMessage = safeMessage;
    }

    public string SafeMessage { get; }
}

public sealed class DocumentUnderstandingValidationException
    : DocumentUnderstandingException
{
    public DocumentUnderstandingValidationException()
        : base(DocumentUnderstandingArchitecture.SafeFailureMessage)
    {
    }
}
