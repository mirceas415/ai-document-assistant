using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TesseractOCR;
using TesseractOCR.Enums;

namespace AI.DocumentAssistant.Server.Ocr;

public sealed class TesseractOcrService : IOcrService, IDisposable
{
    public const string Name = "Tesseract";
    public const string Version = "5.5.1";

    private readonly OcrOptions _configuredOptions;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Engine? _engine;
    private string? _engineLanguages;

    public TesseractOcrService(
        IOptions<OcrOptions> options,
        IHostEnvironment hostEnvironment)
    {
        _configuredOptions = options.Value;
        _hostEnvironment = hostEnvironment;
    }

    public string EngineName => Name;

    public string EngineVersion => Version;

    public async Task<OcrEngineInfo> GetEngineInfoAsync(
        string languages,
        CancellationToken cancellationToken)
    {
        var normalizedLanguages = OcrLanguageConfiguration.Normalize(languages);
        var tessDataPath = ResolveTessDataPath();
        var fingerprints = new List<string>();

        try
        {
            foreach (var language in OcrLanguageConfiguration.Split(normalizedLanguages))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var modelPath = Path.Combine(tessDataPath, $"{language}.traineddata");
                if (!File.Exists(modelPath))
                {
                    throw new OcrUnavailableException(OcrArchitecture.UnavailableMessage);
                }

                await using var model = new FileStream(
                    modelPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 64 * 1024,
                    useAsync: true);
                var hash = await SHA256.HashDataAsync(model, cancellationToken);
                fingerprints.Add($"{language}:{Convert.ToHexString(hash)}");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OcrUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new OcrUnavailableException(
                OcrArchitecture.UnavailableMessage,
                exception);
        }

        var combined = SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join('\n', fingerprints)));
        return new OcrEngineInfo(
            EngineName,
            EngineVersion,
            Convert.ToHexString(combined));
    }

    public async Task<OcrPageResult> OcrPageAsync(
        OcrImage image,
        string languages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        var normalizedLanguages = OcrLanguageConfiguration.Normalize(languages);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureEngine(normalizedLanguages);

            using var pix = TesseractOCR.Pix.Image.LoadFromMemory(image.OpenContent());
            using var page = _engine!.Process(pix);
            cancellationToken.ThrowIfCancellationRequested();

            var text = NormalizeLineEndings(page.Text ?? string.Empty);
            var confidence = NormalizeConfidence(page.MeanConfidence);
            return new OcrPageResult(
                text,
                confidence,
                EngineName,
                _engine.Version ?? EngineVersion);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OcrException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new OcrUnavailableException(
                OcrArchitecture.UnavailableMessage,
                exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _engine?.Dispose();
        _gate.Dispose();
    }

    private void EnsureEngine(string languages)
    {
        if (_engine is not null)
        {
            if (!string.Equals(_engineLanguages, languages, StringComparison.Ordinal))
            {
                throw new OcrUnavailableException(
                    "The local OCR language configuration changed during processing.");
            }

            return;
        }

        _engine = new Engine(
            ResolveTessDataPath(),
            languages,
            EngineMode.Default);
        _engineLanguages = languages;
    }

    private string ResolveTessDataPath()
    {
        var configuredPath = _configuredOptions.ValidatedCopy().TessDataPath;
        return Path.GetFullPath(
            Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(_hostEnvironment.ContentRootPath, configuredPath));
    }

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private static double? NormalizeConfidence(float value)
    {
        if (!float.IsFinite(value))
        {
            return null;
        }

        var normalized = value > 1 ? value / 100d : value;
        return Math.Clamp(normalized, 0d, 1d);
    }
}
