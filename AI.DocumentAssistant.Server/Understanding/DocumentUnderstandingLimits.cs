namespace AI.DocumentAssistant.Server.Understanding;

public static class DocumentUnderstandingLimits
{
    public const int MaximumDocumentSubtypeLength = 100;
    public const int MaximumDetectedTitleLength = 300;
    public const int MaximumSubjectLength = 500;
    public const int MaximumLanguageCodeLength = 35;
    public const int MaximumMetadataEntries = 50;
    public const int MaximumMetadataLabelLength = 100;
    public const int MaximumMetadataValueLength = 500;
    public const int MaximumModelLength = 100;
    public const int MaximumPromptVersionLength = 64;
    public const int SourceContentHashLength = 64;
    public const int MaximumErrorLength = 500;
    public const int MaximumInputTokens = 6_000;
    public const int MinimumUsableTokens = 20;
    public const int MinimumMeaningfulCharacters = 40;
    public const int MaximumOutputTokens = 2_500;
}
