using System.Text.Json.Serialization;

namespace AI.DocumentAssistant.Server.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DocumentType
{
    Unknown,
    Contract,
    Invoice,
    Receipt,
    Report,
    Policy,
    Procedure,
    Manual,
    CourseMaterial,
    ResearchPaper,
    FinancialDocument,
    Form,
    Letter,
    Resume,
    TechnicalDocument,
    Other
}
