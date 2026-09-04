using AI.DocumentAssistant.Server.Models;

namespace AI.DocumentAssistant.Server.Retrieval;

public interface IRetrievalQueryAnalyzer
{
    RetrievalQuery Analyze(string query);
}

public sealed record RetrievalQuery(
    string OriginalText,
    string NormalizedText,
    IReadOnlyList<string> SearchTerms,
    IReadOnlyList<DocumentType> DocumentTypeHints,
    IReadOnlyList<string> IdentifierValues,
    IReadOnlyList<string> DateValues,
    IReadOnlyList<string> MonetaryValues);
