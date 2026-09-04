namespace AI.DocumentAssistant.Server.Ocr;

public sealed class OcrOptions
{
    public const string SectionName = "Ocr";

    public bool Enabled { get; set; } = true;

    public string TessDataPath { get; set; } = "tessdata";

    public string Languages { get; set; } = "ron+eng";

    public int RenderDpi { get; set; } = 300;

    public int MaxCandidatePages { get; set; } = 200;

    public long MaxRenderedPixels { get; set; } = 25_000_000;

    public OcrOptions ValidatedCopy()
    {
        if (string.IsNullOrWhiteSpace(Languages) || Languages.Length > OcrArchitecture.MaximumLanguagesLength)
        {
            throw new OcrUnavailableException(
                "Local OCR languages are not configured correctly.");
        }

        if (string.IsNullOrWhiteSpace(TessDataPath) || TessDataPath.Length > 1_000)
        {
            throw new OcrUnavailableException(
                "The local OCR language-data location is not configured correctly.");
        }

        if (RenderDpi is < 72 or > 600 ||
            MaxCandidatePages is < 1 or > 1_000 ||
            MaxRenderedPixels is < 1_000_000 or > 100_000_000)
        {
            throw new OcrUnavailableException(
                "Local OCR resource limits are not configured correctly.");
        }

        return new OcrOptions
        {
            Enabled = Enabled,
            TessDataPath = TessDataPath.Trim(),
            Languages = OcrLanguageConfiguration.Normalize(Languages),
            RenderDpi = RenderDpi,
            MaxCandidatePages = MaxCandidatePages,
            MaxRenderedPixels = MaxRenderedPixels
        };
    }
}

public static class OcrLanguageConfiguration
{
    public static string Normalize(string languages)
    {
        var values = languages
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length is < 1 or > 8 ||
            values.Any(value => value.Length is < 2 or > 16 ||
                value.Any(character =>
                    !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_'))))
        {
            throw new OcrUnavailableException(
                "Local OCR languages are not configured correctly.");
        }

        return string.Join('+', values);
    }

    public static IReadOnlyList<string> Split(string languages) =>
        Normalize(languages).Split('+');
}
