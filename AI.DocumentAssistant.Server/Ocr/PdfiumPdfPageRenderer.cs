using PDFtoImage;
using SkiaSharp;

#pragma warning disable CA1416 // PDFtoImage supplies the platform-specific PDFium runtime assets.

namespace AI.DocumentAssistant.Server.Ocr;

public sealed class PdfiumPdfPageRenderer : IPdfPageRenderer
{
    public Task<OcrImage> RenderPageAsync(
        Stream pdfStream,
        int pageNumber,
        int requestedDpi,
        long maximumPixels,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdfStream);
        cancellationToken.ThrowIfCancellationRequested();
        if (!pdfStream.CanSeek)
        {
            throw new PdfPageRenderException(
                "The PDF stream cannot be rendered safely for OCR.",
                new NotSupportedException("A seekable PDF stream is required."));
        }

        try
        {
            pdfStream.Position = 0;
            var pageSizes = Conversion.GetPageSizes(
                pdfStream,
                leaveOpen: true,
                password: null);
            if (pageNumber < 1 || pageNumber > pageSizes.Count)
            {
                throw new OcrException("The requested PDF page is not available for OCR.");
            }

            var pageSize = pageSizes[pageNumber - 1];
            var plan = PdfRenderSafety.Calculate(
                pageSize.Width,
                pageSize.Height,
                requestedDpi,
                maximumPixels);

            cancellationToken.ThrowIfCancellationRequested();
            pdfStream.Position = 0;
            using var bitmap = Conversion.ToImage(
                pdfStream,
                pageNumber - 1,
                leaveOpen: true,
                password: null,
                options: new RenderOptions(
                    Dpi: plan.EffectiveDpi,
                    BackgroundColor: SKColors.White));
            cancellationToken.ThrowIfCancellationRequested();

            if ((long)bitmap.Width * bitmap.Height > maximumPixels)
            {
                throw new OcrException(
                    "The rendered PDF page exceeded the configured OCR pixel limit.");
            }

            var encoded = new MemoryStream();
            if (!bitmap.Encode(encoded, SKEncodedImageFormat.Png, 100))
            {
                encoded.Dispose();
                throw new OcrException("The PDF page could not be encoded for local OCR.");
            }

            encoded.Position = 0;
            return Task.FromResult(new OcrImage(
                encoded,
                bitmap.Width,
                bitmap.Height,
                plan.EffectiveDpi));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OcrException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new PdfPageRenderException(
                "The PDF page could not be rendered for local OCR.",
                exception);
        }
    }
}

#pragma warning restore CA1416
