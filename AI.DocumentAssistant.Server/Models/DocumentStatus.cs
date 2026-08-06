using System.Text.Json.Serialization;

namespace AI.DocumentAssistant.Server.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DocumentStatus
{
    Uploaded,
    Processing,
    Ready,
    Failed
}
