namespace AI.DocumentAssistant.Server.Ocr;

public static class PdfRenderSafety
{
    public static PdfRenderPlan Calculate(
        double widthPoints,
        double heightPoints,
        int requestedDpi,
        long maximumPixels)
    {
        if (!double.IsFinite(widthPoints) ||
            !double.IsFinite(heightPoints) ||
            widthPoints <= 0 ||
            heightPoints <= 0 ||
            requestedDpi <= 0 ||
            maximumPixels <= 0)
        {
            throw new OcrException("The PDF page dimensions are invalid for OCR rendering.");
        }

        var requestedWidth = widthPoints * requestedDpi / 72d;
        var requestedHeight = heightPoints * requestedDpi / 72d;
        var requestedPixels = requestedWidth * requestedHeight;
        var scale = requestedPixels > maximumPixels
            ? Math.Sqrt(maximumPixels / requestedPixels)
            : 1d;
        var effectiveDpi = Math.Max(1, (int)Math.Floor(requestedDpi * scale));

        var width = GetPixelDimension(widthPoints, effectiveDpi);
        var height = GetPixelDimension(heightPoints, effectiveDpi);
        while ((long)width * height > maximumPixels && effectiveDpi > 1)
        {
            effectiveDpi--;
            width = GetPixelDimension(widthPoints, effectiveDpi);
            height = GetPixelDimension(heightPoints, effectiveDpi);
        }

        if ((long)width * height > maximumPixels)
        {
            throw new OcrException(
                "The PDF page is too large to render safely for OCR.");
        }

        return new PdfRenderPlan(width, height, effectiveDpi);
    }

    private static int GetPixelDimension(double points, int dpi)
    {
        var pixels = Math.Ceiling(points * dpi / 72d);
        if (!double.IsFinite(pixels) || pixels > int.MaxValue)
        {
            throw new OcrException(
                "The PDF page is too large to render safely for OCR.");
        }

        return Math.Max(1, (int)pixels);
    }
}

public sealed record PdfRenderPlan(
    int WidthPixels,
    int HeightPixels,
    int EffectiveDpi);
