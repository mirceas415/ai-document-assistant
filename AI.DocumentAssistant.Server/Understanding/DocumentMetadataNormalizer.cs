using System.Globalization;
using System.Text;
using AI.DocumentAssistant.Server.Models;

namespace AI.DocumentAssistant.Server.Understanding;

public static class DocumentMetadataNormalizer
{
    private static readonly CultureInfo[] MonthNameCultures =
    [
        CultureInfo.InvariantCulture,
        CultureInfo.GetCultureInfo("ro-RO"),
        CultureInfo.GetCultureInfo("en-GB"),
        CultureInfo.GetCultureInfo("en-US"),
        CultureInfo.GetCultureInfo("de-DE"),
        CultureInfo.GetCultureInfo("it-IT"),
        CultureInfo.GetCultureInfo("fr-FR"),
        CultureInfo.GetCultureInfo("es-ES")
    ];

    private static readonly string[] MonthNameFormats =
    [
        "d MMMM yyyy",
        "d MMM yyyy",
        "MMMM d yyyy",
        "MMMM d, yyyy",
        "MMM d yyyy",
        "MMM d, yyyy"
    ];

    public static string CollapseWhitespace(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    public static string NormalizeLabel(string value)
    {
        var collapsed = CollapseWhitespace(value);
        var builder = new StringBuilder(collapsed.Length);
        var pendingSeparator = false;

        foreach (var character in collapsed)
        {
            if (character is '-' or '_' || char.IsWhiteSpace(character))
            {
                pendingSeparator = builder.Length > 0;
                continue;
            }

            if (!char.IsAsciiLetterOrDigit(character))
            {
                throw new DocumentUnderstandingValidationException();
            }

            if (pendingSeparator)
            {
                builder.Append('_');
                pendingSeparator = false;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    public static string? NormalizeValue(
        DocumentMetadataKind kind,
        string cleanedValue) =>
        kind == DocumentMetadataKind.Date
            ? TryNormalizeDate(cleanedValue)
            : kind == DocumentMetadataKind.MonetaryAmount
                ? null
                : cleanedValue;

    public static string? TryNormalizeDate(string value)
    {
        var cleaned = CollapseWhitespace(value);

        if (DateOnly.TryParseExact(
                cleaned,
                ["yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var isoDate))
        {
            return isoDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (DateOnly.TryParseExact(
                cleaned,
                ["d.M.yyyy", "dd.MM.yyyy"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dottedDate))
        {
            return dottedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        foreach (var culture in MonthNameCultures)
        {
            if (DateOnly.TryParseExact(
                    cleaned,
                    MonthNameFormats,
                    culture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out var namedDate))
            {
                return namedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        var numericParts = cleaned.Split(['/', '-']);
        if (numericParts.Length != 3 ||
            !int.TryParse(numericParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var first) ||
            !int.TryParse(numericParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var second) ||
            !int.TryParse(numericParts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            numericParts[2].Length != 4)
        {
            return null;
        }

        int day;
        int month;
        if (first > 12 && second is >= 1 and <= 12)
        {
            day = first;
            month = second;
        }
        else if (second > 12 && first is >= 1 and <= 12)
        {
            day = second;
            month = first;
        }
        else
        {
            return null;
        }

        return DateOnly.TryParseExact(
            $"{year:D4}-{month:D2}-{day:D2}",
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var unambiguousDate)
            ? unambiguousDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }
}
