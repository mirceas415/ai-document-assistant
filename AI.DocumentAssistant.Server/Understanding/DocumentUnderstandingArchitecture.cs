namespace AI.DocumentAssistant.Server.Understanding;

public static class DocumentUnderstandingArchitecture
{
    public const string PromptVersion = "document-understanding-v1";
    public const string InsufficientTextReason = "Insufficient normalized text.";
    public const string SafeFailureMessage =
        "Document understanding could not be completed. Please retry.";
}
