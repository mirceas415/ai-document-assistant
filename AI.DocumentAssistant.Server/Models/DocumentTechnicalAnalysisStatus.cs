using System.Text.Json.Serialization;

namespace AI.DocumentAssistant.Server.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DocumentTechnicalAnalysisStatus
{
    NotAnalyzed,
    Processing,
    Ready,
    Failed,
    Skipped
}
