using System.Text.Json.Serialization;

namespace AI.DocumentAssistant.Server.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DocumentPageOcrStatus
{
    Ready,
    Empty,
    Failed,
    SkippedLimit
}
