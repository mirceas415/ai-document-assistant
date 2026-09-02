using System.Text.Json.Serialization;

namespace AI.DocumentAssistant.Server.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DocumentUnderstandingStatus
{
    NotAnalyzed,
    Pending,
    Processing,
    Ready,
    Failed,
    Skipped
}
