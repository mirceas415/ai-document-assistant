namespace AI.DocumentAssistant.Server.Retrieval;

public sealed class RetrievalRerankingException : Exception
{
    public RetrievalRerankingException(string message)
        : base(message)
    {
    }

    public RetrievalRerankingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
