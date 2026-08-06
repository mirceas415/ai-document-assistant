using Microsoft.ML.Tokenizers;

namespace AI.DocumentAssistant.Server.Chunking;

public sealed class Cl100kDocumentTokenizer : IDocumentTokenizer
{
    private readonly Tokenizer _tokenizer = TiktokenTokenizer.CreateForEncoding("cl100k_base");

    public int CountTokens(string text) => _tokenizer.CountTokens(text);

    public int GetIndexByTokenCount(string text, int maximumTokenCount) =>
        _tokenizer.GetIndexByTokenCount(
            text,
            maximumTokenCount,
            out _,
            out _);

    public int GetIndexByTokenCountFromEnd(string text, int maximumTokenCount) =>
        _tokenizer.GetIndexByTokenCountFromEnd(
            text,
            maximumTokenCount,
            out _,
            out _);
}
