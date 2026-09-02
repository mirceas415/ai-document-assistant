using AI.DocumentAssistant.Server.Models;

namespace AI.DocumentAssistant.Server.TechnicalAnalysis;

public static class PdfTechnicalClassifier
{
    public static PdfTextMetrics MeasureText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new PdfTextMetrics(0, 0);
        }

        var alphanumericCharacterCount = 0;
        var usefulWordCount = 0;
        var currentWordLength = 0;

        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                alphanumericCharacterCount++;
                currentWordLength++;
                continue;
            }

            if (currentWordLength >=
                PdfTechnicalAnalysisHeuristics.MinimumUsefulWordAlphanumericLength)
            {
                usefulWordCount++;
            }

            currentWordLength = 0;
        }

        if (currentWordLength >=
            PdfTechnicalAnalysisHeuristics.MinimumUsefulWordAlphanumericLength)
        {
            usefulWordCount++;
        }

        return new PdfTextMetrics(
            alphanumericCharacterCount,
            usefulWordCount);
    }

    public static bool HasMeaningfulText(
        int textCharacterCount,
        int wordCount) =>
        textCharacterCount >=
            PdfTechnicalAnalysisHeuristics.MinimumAlphanumericCharacterCount ||
        wordCount >= PdfTechnicalAnalysisHeuristics.MinimumUsefulWordCount;

    public static double ClampCoverage(double imageCoverageRatio)
    {
        if (!double.IsFinite(imageCoverageRatio))
        {
            return 0;
        }

        return Math.Clamp(imageCoverageRatio, 0, 1);
    }

    public static PdfPageTechnicalAnalysisResult ClassifyPage(
        PdfPageTechnicalMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        if (metrics.PageNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(metrics),
                "A PDF page number must be positive.");
        }

        if (metrics.TextCharacterCount < 0 ||
            metrics.WordCount < 0 ||
            metrics.ImageCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(metrics),
                "PDF page metrics cannot be negative.");
        }

        var coverage = ClampCoverage(metrics.ImageCoverageRatio);
        var hasMeaningfulText = HasMeaningfulText(
            metrics.TextCharacterCount,
            metrics.WordCount);
        var hasPageSizedImage = metrics.ImageCount > 0 &&
            coverage >= PdfTechnicalAnalysisHeuristics.PageSizedImageCoverageThreshold;

        var technicalType = hasMeaningfulText
            ? coverage >= PdfTechnicalAnalysisHeuristics.SubstantialImageCoverageThreshold
                ? TechnicalType.Mixed
                : TechnicalType.TextBased
            : hasPageSizedImage
                ? TechnicalType.Scanned
                : metrics.ImageCount > 0
                    ? TechnicalType.ImageBased
                    : TechnicalType.Unknown;

        return new PdfPageTechnicalAnalysisResult(
            metrics.PageNumber,
            technicalType,
            metrics.TextCharacterCount,
            metrics.WordCount,
            metrics.ImageCount,
            coverage,
            hasMeaningfulText,
            hasPageSizedImage);
    }

    public static TechnicalType ClassifyDocument(
        IReadOnlyList<PdfPageTechnicalAnalysisResult> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);

        if (pages.Count == 0)
        {
            return TechnicalType.Unknown;
        }

        var unknownCount = pages.Count(page =>
            page.TechnicalType == TechnicalType.Unknown);
        var knownPages = pages
            .Where(page => page.TechnicalType != TechnicalType.Unknown)
            .ToArray();

        if (knownPages.Length == 0)
        {
            return TechnicalType.Unknown;
        }

        var unknownTolerance = Math.Max(
            PdfTechnicalAnalysisHeuristics.MinimumUnknownPageTolerance,
            (int)Math.Floor(
                pages.Count * PdfTechnicalAnalysisHeuristics.MaximumUnknownPageRatio));
        if (unknownCount > unknownTolerance)
        {
            return TechnicalType.Unknown;
        }

        var groups = knownPages
            .GroupBy(page => page.TechnicalType)
            .Select(group => new { Type = group.Key, Count = group.Count() })
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Type)
            .ToArray();
        var dominant = groups[0];
        var dominantRatio = dominant.Count / (double)knownPages.Length;

        if (dominantRatio >=
            PdfTechnicalAnalysisHeuristics.DocumentStrongMajorityThreshold)
        {
            return dominant.Type;
        }

        return groups.Length > 1
            ? TechnicalType.Mixed
            : TechnicalType.Unknown;
    }
}
