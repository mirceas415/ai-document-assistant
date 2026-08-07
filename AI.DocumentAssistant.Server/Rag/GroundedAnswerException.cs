namespace AI.DocumentAssistant.Server.Rag;

public sealed class GroundedAnswerException : Exception
{
    public GroundedAnswerException(string safeMessage)
        : base(safeMessage)
    {
        SafeMessage = safeMessage;
    }

    public GroundedAnswerException(string safeMessage, Exception innerException)
        : base(safeMessage, innerException)
    {
        SafeMessage = safeMessage;
    }

    public string SafeMessage { get; }
}
