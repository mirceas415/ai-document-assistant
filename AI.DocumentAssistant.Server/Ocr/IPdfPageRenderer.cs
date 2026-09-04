namespace AI.DocumentAssistant.Server.Ocr;

public interface IPdfPageRenderer
{
    Task<OcrImage> RenderPageAsync(
        Stream pdfStream,
        int pageNumber,
        int requestedDpi,
        long maximumPixels,
        CancellationToken cancellationToken);
}

public sealed class OcrImage : IDisposable
{
    private MemoryStream? _content;

    public OcrImage(
        MemoryStream content,
        int widthPixels,
        int heightPixels,
        int effectiveDpi)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
        WidthPixels = widthPixels;
        HeightPixels = heightPixels;
        EffectiveDpi = effectiveDpi;
    }

    public int WidthPixels { get; }

    public int HeightPixels { get; }

    public int EffectiveDpi { get; }

    public MemoryStream OpenContent()
    {
        var content = _content ?? throw new ObjectDisposedException(nameof(OcrImage));
        content.Position = 0;
        return content;
    }

    public void Dispose()
    {
        _content?.Dispose();
        _content = null;
    }
}
