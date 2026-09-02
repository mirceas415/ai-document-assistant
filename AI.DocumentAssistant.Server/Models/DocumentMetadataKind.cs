using System.Text.Json.Serialization;

namespace AI.DocumentAssistant.Server.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DocumentMetadataKind
{
    Organization,
    Person,
    Identifier,
    Date,
    MonetaryAmount,
    Jurisdiction,
    Topic,
    Other
}
