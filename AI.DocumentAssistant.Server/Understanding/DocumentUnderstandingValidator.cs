using AI.DocumentAssistant.Server.Models;

namespace AI.DocumentAssistant.Server.Understanding;

public sealed class DocumentUnderstandingValidator
{
    public ValidatedDocumentUnderstanding Validate(
        DocumentUnderstandingProviderResult providerResult)
    {
        ArgumentNullException.ThrowIfNull(providerResult);

        var documentType = ParseEnum<DocumentType>(providerResult.DocumentType);
        var documentTypeConfidence = ValidateRequiredConfidence(
            providerResult.DocumentTypeConfidence);
        var languageCode = NormalizeLanguageCode(providerResult.PrimaryLanguageCode);
        var languageConfidence = ValidateRequiredConfidence(
            providerResult.LanguageConfidence);
        var subtype = ValidateOptionalText(
            providerResult.DocumentSubtype,
            DocumentUnderstandingLimits.MaximumDocumentSubtypeLength);
        var detectedTitle = ValidateOptionalText(
            providerResult.DetectedTitle,
            DocumentUnderstandingLimits.MaximumDetectedTitleLength);
        var subject = ValidateOptionalText(
            providerResult.Subject,
            DocumentUnderstandingLimits.MaximumSubjectLength);

        if (providerResult.Metadata is null ||
            providerResult.Metadata.Count > DocumentUnderstandingLimits.MaximumMetadataEntries)
        {
            throw new DocumentUnderstandingValidationException();
        }

        var metadata = new List<ValidatedDocumentMetadataEntry>(
            providerResult.Metadata.Count);
        var deduplicationKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in providerResult.Metadata)
        {
            if (entry is null)
            {
                throw new DocumentUnderstandingValidationException();
            }

            var kind = ParseEnum<DocumentMetadataKind>(entry.Kind);
            var label = DocumentMetadataNormalizer.NormalizeLabel(
                entry.Label ?? throw new DocumentUnderstandingValidationException());
            var value = DocumentMetadataNormalizer.CollapseWhitespace(
                entry.Value ?? throw new DocumentUnderstandingValidationException());

            if (label.Length == 0 ||
                label.Length > DocumentUnderstandingLimits.MaximumMetadataLabelLength ||
                value.Length == 0 ||
                value.Length > DocumentUnderstandingLimits.MaximumMetadataValueLength)
            {
                throw new DocumentUnderstandingValidationException();
            }

            var confidence = ValidateOptionalConfidence(entry.Confidence);
            var normalizedValue = DocumentMetadataNormalizer.NormalizeValue(kind, value);
            if (normalizedValue?.Length > DocumentUnderstandingLimits.MaximumMetadataValueLength)
            {
                throw new DocumentUnderstandingValidationException();
            }

            var deduplicationKey = string.Join(
                '\u001F',
                kind.ToString(),
                label,
                normalizedValue ?? value);
            if (!deduplicationKeys.Add(deduplicationKey))
            {
                continue;
            }

            metadata.Add(new ValidatedDocumentMetadataEntry(
                kind,
                label,
                value,
                normalizedValue,
                confidence,
                metadata.Count));
        }

        return new ValidatedDocumentUnderstanding(
            documentType,
            subtype,
            documentTypeConfidence,
            languageCode,
            languageConfidence,
            detectedTitle,
            subject,
            metadata);
    }

    public static string NormalizeLanguageCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DocumentUnderstandingValidationException();
        }

        var parts = value.Trim().Split('-');
        if (parts.Length == 0 ||
            parts.Any(part => part.Length is < 2 or > 8 || !part.All(char.IsAsciiLetterOrDigit)) ||
            parts[0].Length is < 2 or > 3 ||
            !parts[0].All(char.IsAsciiLetter))
        {
            throw new DocumentUnderstandingValidationException();
        }

        var normalized = new string[parts.Length];
        normalized[0] = parts[0].ToLowerInvariant();
        for (var index = 1; index < parts.Length; index++)
        {
            normalized[index] = parts[index].Length == 4 && parts[index].All(char.IsAsciiLetter)
                ? char.ToUpperInvariant(parts[index][0]) + parts[index][1..].ToLowerInvariant()
                : parts[index].Length == 2 && parts[index].All(char.IsAsciiLetter)
                    ? parts[index].ToUpperInvariant()
                    : parts[index].ToLowerInvariant();
        }

        var result = string.Join('-', normalized);
        if (result.Length > DocumentUnderstandingLimits.MaximumLanguageCodeLength)
        {
            throw new DocumentUnderstandingValidationException();
        }

        return result;
    }

    private static TEnum ParseEnum<TEnum>(string? value)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            throw new DocumentUnderstandingValidationException();
        }

        return parsed;
    }

    private static double ValidateRequiredConfidence(double? confidence) =>
        ValidateOptionalConfidence(confidence) ??
        throw new DocumentUnderstandingValidationException();

    private static double? ValidateOptionalConfidence(double? confidence)
    {
        if (confidence is null)
        {
            return null;
        }

        if (!double.IsFinite(confidence.Value) || confidence is < 0 or > 1)
        {
            throw new DocumentUnderstandingValidationException();
        }

        return confidence;
    }

    private static string? ValidateOptionalText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = DocumentMetadataNormalizer.CollapseWhitespace(value);
        if (cleaned.Length > maximumLength)
        {
            throw new DocumentUnderstandingValidationException();
        }

        return cleaned;
    }
}

public sealed record ValidatedDocumentUnderstanding(
    DocumentType DocumentType,
    string? DocumentSubtype,
    double DocumentTypeConfidence,
    string PrimaryLanguageCode,
    double LanguageConfidence,
    string? DetectedTitle,
    string? Subject,
    IReadOnlyList<ValidatedDocumentMetadataEntry> Metadata);

public sealed record ValidatedDocumentMetadataEntry(
    DocumentMetadataKind Kind,
    string Label,
    string Value,
    string? NormalizedValue,
    double? Confidence,
    int Sequence);
