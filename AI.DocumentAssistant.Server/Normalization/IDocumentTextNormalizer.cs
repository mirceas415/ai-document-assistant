namespace AI.DocumentAssistant.Server.Normalization;

public interface IDocumentTextNormalizer
{
    DocumentNormalizationResult Normalize(
        IReadOnlyList<NormalizationSourceSection> sections,
        bool isPdf,
        CancellationToken cancellationToken = default);
}
