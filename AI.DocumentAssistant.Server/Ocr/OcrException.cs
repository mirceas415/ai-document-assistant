namespace AI.DocumentAssistant.Server.Ocr;

public class OcrException : Exception
{
    public OcrException(string safeMessage)
        : base(safeMessage)
    {
        SafeMessage = safeMessage;
    }

    public OcrException(string safeMessage, Exception innerException)
        : base(safeMessage, innerException)
    {
        SafeMessage = safeMessage;
    }

    public string SafeMessage { get; }
}

public sealed class OcrUnavailableException : OcrException
{
    public OcrUnavailableException(string safeMessage)
        : base(safeMessage)
    {
    }

    public OcrUnavailableException(string safeMessage, Exception innerException)
        : base(safeMessage, innerException)
    {
    }
}

public sealed class PdfPageRenderException : OcrException
{
    public PdfPageRenderException(string safeMessage, Exception innerException)
        : base(safeMessage, innerException)
    {
    }
}
