using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;

namespace AI.DocumentAssistant.Server.TechnicalAnalysis;

public sealed class PdfPigPdfTechnicalAnalyzer : IPdfTechnicalAnalyzer
{
    public string AnalyzerVersion => PdfTechnicalAnalysisArchitecture.AnalyzerVersion;

    public Task<PdfTechnicalAnalysisResult> AnalyzeAsync(
        Stream pdfStream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdfStream);
        cancellationToken.ThrowIfCancellationRequested();

        if (!pdfStream.CanRead)
        {
            throw new PdfTechnicalAnalysisException(
                PdfTechnicalAnalysisArchitecture.SafeFailureMessage);
        }

        try
        {
            using var pdfDocument = PdfDocument.Open(pdfStream);
            var pageResults = new List<PdfPageTechnicalAnalysisResult>();

            foreach (var page in pdfDocument.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var textMetrics = PdfTechnicalClassifier.MeasureText(page.Text);
                var imageCount = 0;
                var largestVisibleImageCoverage = 0d;
                var pageBounds = page.CropBox.Bounds;

                foreach (var image in page.GetImages())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (image.IsImageMask ||
                        image.WidthInSamples <= 0 ||
                        image.HeightInSamples <= 0)
                    {
                        continue;
                    }

                    imageCount++;
                    largestVisibleImageCoverage = Math.Max(
                        largestVisibleImageCoverage,
                        CalculateVisibleCoverage(pageBounds, image.BoundingBox));
                }

                pageResults.Add(PdfTechnicalClassifier.ClassifyPage(
                    new PdfPageTechnicalMetrics(
                        page.Number,
                        textMetrics.AlphanumericCharacterCount,
                        textMetrics.UsefulWordCount,
                        imageCount,
                        largestVisibleImageCoverage)));
            }

            return Task.FromResult(new PdfTechnicalAnalysisResult(
                PdfTechnicalClassifier.ClassifyDocument(pageResults),
                pageResults));
        }
        catch (PdfTechnicalAnalysisException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new PdfTechnicalAnalysisException(
                PdfTechnicalAnalysisArchitecture.SafeFailureMessage,
                exception);
        }
    }

    private static double CalculateVisibleCoverage(
        PdfRectangle pageRectangle,
        PdfRectangle imageRectangle)
    {
        var page = ToAxisAlignedBounds(pageRectangle);
        var image = ToAxisAlignedBounds(imageRectangle);
        var pageWidth = page.Right - page.Left;
        var pageHeight = page.Top - page.Bottom;

        if (!double.IsFinite(pageWidth) ||
            !double.IsFinite(pageHeight) ||
            pageWidth <= 0 ||
            pageHeight <= 0)
        {
            return 0;
        }

        var intersectionWidth = Math.Max(
            0,
            Math.Min(page.Right, image.Right) - Math.Max(page.Left, image.Left));
        var intersectionHeight = Math.Max(
            0,
            Math.Min(page.Top, image.Top) - Math.Max(page.Bottom, image.Bottom));
        var ratio = intersectionWidth * intersectionHeight / (pageWidth * pageHeight);

        return PdfTechnicalClassifier.ClampCoverage(ratio);
    }

    private static AxisAlignedBounds ToAxisAlignedBounds(PdfRectangle rectangle)
    {
        var left = Math.Min(
            Math.Min(rectangle.BottomLeft.X, rectangle.BottomRight.X),
            Math.Min(rectangle.TopLeft.X, rectangle.TopRight.X));
        var right = Math.Max(
            Math.Max(rectangle.BottomLeft.X, rectangle.BottomRight.X),
            Math.Max(rectangle.TopLeft.X, rectangle.TopRight.X));
        var bottom = Math.Min(
            Math.Min(rectangle.BottomLeft.Y, rectangle.BottomRight.Y),
            Math.Min(rectangle.TopLeft.Y, rectangle.TopRight.Y));
        var top = Math.Max(
            Math.Max(rectangle.BottomLeft.Y, rectangle.BottomRight.Y),
            Math.Max(rectangle.TopLeft.Y, rectangle.TopRight.Y));

        return new AxisAlignedBounds(left, right, bottom, top);
    }

    private sealed record AxisAlignedBounds(
        double Left,
        double Right,
        double Bottom,
        double Top);
}
