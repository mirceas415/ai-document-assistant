using System.Text.Json.Serialization;

namespace AI.DocumentAssistant.Server.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DocumentOcrStatus
{
    NotAnalyzed,
    Processing,
    Ready,
    Partial,
    Failed,
    Skipped
}
