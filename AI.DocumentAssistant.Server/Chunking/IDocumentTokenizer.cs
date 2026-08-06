namespace AI.DocumentAssistant.Server.Chunking;

public interface IDocumentTokenizer
{
    int CountTokens(string text);

    int GetIndexByTokenCount(string text, int maximumTokenCount);

    int GetIndexByTokenCountFromEnd(string text, int maximumTokenCount);
}
