namespace AI.DocumentAssistant.Server.Ocr;

public interface IOcrService
{
    string EngineName { get; }

    string EngineVersion { get; }

    Task<OcrEngineInfo> GetEngineInfoAsync(
        string languages,
        CancellationToken cancellationToken);

    Task<OcrPageResult> OcrPageAsync(
        OcrImage image,
        string languages,
        CancellationToken cancellationToken);
}

public sealed record OcrEngineInfo(
    string EngineName,
    string EngineVersion,
    string ModelFingerprint);

public sealed record OcrPageResult(
    string Text,
    double? MeanConfidence,
    string EngineName,
    string EngineVersion);
