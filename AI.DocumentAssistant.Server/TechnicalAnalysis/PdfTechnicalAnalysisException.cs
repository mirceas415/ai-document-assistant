namespace AI.DocumentAssistant.Server.TechnicalAnalysis;

public sealed class PdfTechnicalAnalysisException : Exception
{
    public PdfTechnicalAnalysisException(string safeMessage)
        : base(safeMessage)
    {
        SafeMessage = safeMessage;
    }

    public PdfTechnicalAnalysisException(string safeMessage, Exception innerException)
        : base(safeMessage, innerException)
    {
        SafeMessage = safeMessage;
    }

    public string SafeMessage { get; }
}
