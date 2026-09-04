using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Retrieval;

namespace AI.DocumentAssistant.Server.Tests;

public sealed class RetrievalQueryAnalyzerTests
{
    private readonly DeterministicRetrievalQueryAnalyzer _analyzer = new();

    [Theory]
    [InlineData("contractul Vodafone", DocumentType.Contract)]
    [InlineData("factură restantă", DocumentType.Invoice)]
    [InlineData("annual report", DocumentType.Report)]
    [InlineData("politică de securitate", DocumentType.Policy)]
    [InlineData("procedură internă", DocumentType.Procedure)]
    [InlineData("user manual", DocumentType.Manual)]
    [InlineData("formular fiscal", DocumentType.Form)]
    [InlineData("scrisoare oficială", DocumentType.Letter)]
    [InlineData("candidate CV", DocumentType.Resume)]
    [InlineData("research paper methods", DocumentType.ResearchPaper)]
    [InlineData("material de curs", DocumentType.CourseMaterial)]
    public void ExplicitAliasesProduceControlledSoftDocumentTypeHints(
        string query,
        DocumentType expected)
    {
        var result = _analyzer.Analyze(query);

        Assert.Contains(expected, result.DocumentTypeHints);
    }

    [Fact]
    public void IdentifierTokensKeepMeaningfulSeparatorsAndNormalizeCase()
    {
        var result = _analyzer.Analyze(
            "What do CN-2026-00491, INV/2026/118 and AB_9917 say?");

        Assert.Equal(
            ["cn-2026-00491", "inv/2026/118", "ab_9917"],
            result.IdentifierValues);
    }

    [Fact]
    public void SignificantLexicalTermsDropConversationalFillerButKeepIdentifierParts()
    {
        var result = _analyzer.Analyze(
            "What does contract CN-2026-00491 say about termination?");

        Assert.Equal(
            ["contract", "cn", "2026", "00491", "termination"],
            result.SearchTerms);
    }

    [Fact]
    public void DatesAndAmountsUseConservativeDeterministicNormalization()
    {
        var result = _analyzer.Analyze(
            "Compare 2026-04-01 with 01.04.2026 and find 18,500 EUR or 18500 EUR.");

        Assert.Equal(["2026-04-01"], result.DateValues);
        Assert.Equal(["18500EUR"], result.MonetaryValues);
    }

    [Fact]
    public void RomanianDiacriticsAndUnicodeArePreservedInNormalizedTerms()
    {
        var result = _analyzer.Analyze("  Încetare Vodafone în România 📄  ");

        Assert.Equal("Încetare Vodafone în România 📄", result.OriginalText);
        Assert.Equal("încetare vodafone în românia 📄", result.NormalizedText);
        Assert.Contains("încetare", result.SearchTerms);
        Assert.Contains("românia", result.SearchTerms);
    }

    [Theory]
    [InlineData("\"foo OR bar\"")]
    [InlineData("what's in section (4.2)?")]
    [InlineData("/// (( -- '' ))")]
    [InlineData("CN-2026-00491")]
    [InlineData("șțăîâ Unicode 東京")]
    public void QuotesOperatorsPunctuationAndUnicodeNeverCauseLocalParsingErrors(
        string query)
    {
        var exception = Record.Exception(() => _analyzer.Analyze(query));

        Assert.Null(exception);
    }

    [Fact]
    public void SearchHintsAreBounded()
    {
        var query = string.Join(' ', Enumerable.Range(0, 50).Select(index => $"term{index}"));

        var result = _analyzer.Analyze(query);

        Assert.Equal(12, result.SearchTerms.Count);
    }
}
