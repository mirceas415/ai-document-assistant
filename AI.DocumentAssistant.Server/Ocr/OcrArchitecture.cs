using System.Security.Cryptography;
using System.Text;
using AI.DocumentAssistant.Server.Models;

namespace AI.DocumentAssistant.Server.Ocr;

public static class OcrArchitecture
{
    public const string RoutingVersion = "ocr-routing-v1";
    public const string PdfContentType = "application/pdf";
    public const int HashLength = 64;
    public const int MaximumEngineNameLength = 100;
    public const int MaximumEngineVersionLength = 100;
    public const int MaximumLanguagesLength = 100;
    public const int MaximumRoutingVersionLength = 100;
    public const int MaximumErrorLength = 500;

    public const string UnavailableMessage =
        "Local OCR is unavailable. Verify the OCR runtime and configured language data.";

    public const string FailedMessage =
        "Local OCR could not recover text from the selected scanned pages.";

    public const string InterruptedMessage =
        "Local OCR was interrupted. Please retry.";

    public const string RoutingUnavailableMessage =
        "Technical PDF analysis is required before OCR pages can be selected.";

    public static bool IsPdf(string contentType) =>
        string.Equals(contentType, PdfContentType, StringComparison.OrdinalIgnoreCase);

    public static string ComputeConfigurationHash(
        OcrOptions options,
        OcrEngineInfo engineInfo,
        string languages)
    {
        var value = string.Join(
            '\n',
            $"enabled={options.Enabled}",
            $"engine={engineInfo.EngineName}",
            $"engineVersion={engineInfo.EngineVersion}",
            $"model={engineInfo.ModelFingerprint}",
            $"languages={languages}",
            $"renderDpi={options.RenderDpi}",
            $"maxCandidatePages={options.MaxCandidatePages}",
            $"maxRenderedPixels={options.MaxRenderedPixels}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    public static string ComputeUnavailableConfigurationHash(
        OcrOptions options,
        string engineName,
        string engineVersion,
        string languages)
    {
        var info = new OcrEngineInfo(engineName, engineVersion, "unavailable");
        return ComputeConfigurationHash(options, info, languages);
    }

    public static DocumentOcrStatus GetCompletedStatus(
        int candidatePageCount,
        int successfulPageCount) =>
        candidatePageCount == 0
            ? DocumentOcrStatus.Skipped
            : successfulPageCount == candidatePageCount
                ? DocumentOcrStatus.Ready
                : successfulPageCount > 0
                    ? DocumentOcrStatus.Partial
                    : DocumentOcrStatus.Failed;
}
