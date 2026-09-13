using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Ocr;

/// <summary>
/// Uses the system Tesseract executable on Unix, where the Windows-native
/// binaries bundled by TesseractOCR cannot be loaded.
/// </summary>
public sealed class TesseractCliOcrService : IOcrService
{
    public const string Name = "Tesseract";
    private const string ExecutableName = "tesseract";

    private readonly OcrOptions _configuredOptions;
    private readonly IHostEnvironment _hostEnvironment;
    private string? _engineVersion;

    public TesseractCliOcrService(
        IOptions<OcrOptions> options,
        IHostEnvironment hostEnvironment)
    {
        _configuredOptions = options.Value;
        _hostEnvironment = hostEnvironment;
    }

    public string EngineName => Name;

    public string EngineVersion => _engineVersion ?? "CLI";

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

            var version = await GetVersionAsync(cancellationToken);
            var combined = SHA256.HashData(
                Encoding.UTF8.GetBytes(string.Join('\n', fingerprints)));
            return new OcrEngineInfo(
                EngineName,
                version,
                Convert.ToHexString(combined));
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
    }

    public async Task<OcrPageResult> OcrPageAsync(
        OcrImage image,
        string languages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        var normalizedLanguages = OcrLanguageConfiguration.Normalize(languages);

        try
        {
            var version = await GetVersionAsync(cancellationToken);
            var result = await RunAsync(
                [
                    "stdin",
                    "stdout",
                    "--tessdata-dir",
                    ResolveTessDataPath(),
                    "-l",
                    normalizedLanguages,
                    "tsv"
                ],
                image.OpenContent(),
                cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new OcrException(OcrArchitecture.FailedMessage);
            }

            var parsed = ParseTsv(result.StandardOutput);
            return new OcrPageResult(
                parsed.Text,
                parsed.MeanConfidence,
                EngineName,
                version);
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
    }

    private async Task<string> GetVersionAsync(CancellationToken cancellationToken)
    {
        if (_engineVersion is not null)
        {
            return _engineVersion;
        }

        var result = await RunAsync(["--version"], null, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new OcrUnavailableException(OcrArchitecture.UnavailableMessage);
        }

        var firstLine = ReadFirstNonEmptyLine(result.StandardOutput) ??
            ReadFirstNonEmptyLine(result.StandardError);
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            throw new OcrUnavailableException(OcrArchitecture.UnavailableMessage);
        }

        const string prefix = "tesseract ";
        var version = firstLine.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? firstLine[prefix.Length..].Trim()
            : firstLine.Trim();
        _engineVersion = version.Length <= OcrArchitecture.MaximumEngineVersionLength
            ? version
            : version[..OcrArchitecture.MaximumEngineVersionLength];
        return _engineVersion;
    }

    private async Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        Stream? standardInput,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutableName,
            UseShellExecute = false,
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new OcrUnavailableException(OcrArchitecture.UnavailableMessage);
            }
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

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            if (standardInput is not null)
            {
                await standardInput.CopyToAsync(
                    process.StandardInput.BaseStream,
                    cancellationToken);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(cancellationToken);
            return new ProcessResult(
                process.ExitCode,
                await outputTask,
                await errorTask);
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            throw;
        }
        catch
        {
            TryTerminate(process);
            throw;
        }
    }

    private string ResolveTessDataPath()
    {
        var configuredPath = _configuredOptions.ValidatedCopy().TessDataPath;
        return Path.GetFullPath(
            Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(_hostEnvironment.ContentRootPath, configuredPath));
    }

    private static ParsedTsv ParseTsv(string value)
    {
        var text = new StringBuilder();
        var confidences = new List<double>();
        (int Page, int Block, int Paragraph, int Line)? previous = null;

        using var reader = new StringReader(value);
        while (reader.ReadLine() is { } row)
        {
            var fields = row.Split('\t', 12, StringSplitOptions.None);
            if (fields.Length != 12 ||
                !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) ||
                level != 5 ||
                !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var page) ||
                !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var block) ||
                !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var paragraph) ||
                !int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var line))
            {
                continue;
            }

            var word = fields[11].Trim();
            if (word.Length == 0)
            {
                continue;
            }

            var current = (page, block, paragraph, line);
            if (previous is { } prior)
            {
                if (prior.Page != current.page ||
                    prior.Block != current.block ||
                    prior.Paragraph != current.paragraph)
                {
                    text.Append("\n\n");
                }
                else if (prior.Line != current.line)
                {
                    text.Append('\n');
                }
                else
                {
                    text.Append(' ');
                }
            }

            text.Append(word);
            previous = current;

            if (double.TryParse(
                    fields[10],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var confidence) &&
                confidence >= 0)
            {
                confidences.Add(Math.Clamp(confidence / 100d, 0d, 1d));
            }
        }

        return new ParsedTsv(
            text.ToString(),
            confidences.Count == 0 ? null : confidences.Average());
    }

    private static string? ReadFirstNonEmptyLine(string value)
    {
        using var reader = new StringReader(value);
        while (reader.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }

        return null;
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup while preserving the original exception.
        }
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed record ParsedTsv(string Text, double? MeanConfidence);
}
