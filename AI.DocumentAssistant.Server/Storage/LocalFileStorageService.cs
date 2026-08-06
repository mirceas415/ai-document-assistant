namespace AI.DocumentAssistant.Server.Storage;

public sealed class LocalFileStorageService : IFileStorageService
{
    private readonly string _uploadsRoot;

    public LocalFileStorageService(IWebHostEnvironment environment)
    {
        _uploadsRoot = Path.GetFullPath(
            Path.Combine(environment.ContentRootPath, "Uploads"));
    }

    public async Task<string> SaveAsync(
        Stream source,
        string fileExtension,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var normalizedExtension = NormalizeExtension(fileExtension);
        Directory.CreateDirectory(_uploadsRoot);

        var storedFileName = $"{Guid.NewGuid():N}{normalizedExtension}";
        var destinationPath = GetStoragePath(storedFileName);

        try
        {
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81_920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await source.CopyToAsync(destination, cancellationToken);
            return storedFileName;
        }
        catch
        {
            File.Delete(destinationPath);
            throw;
        }
    }

    public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(GetStoragePath(storedFileName));
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storedFileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(GetStoragePath(storedFileName)));
    }

    private string GetStoragePath(string storedFileName)
    {
        if (string.IsNullOrWhiteSpace(storedFileName) ||
            !string.Equals(storedFileName, Path.GetFileName(storedFileName), StringComparison.Ordinal))
        {
            throw new ArgumentException("The stored filename is invalid.", nameof(storedFileName));
        }

        var path = Path.GetFullPath(Path.Combine(_uploadsRoot, storedFileName));
        var requiredPrefix = _uploadsRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!path.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The storage path is outside the uploads directory.");
        }

        return path;
    }

    private static string NormalizeExtension(string fileExtension)
    {
        var extension = fileExtension.StartsWith('.')
            ? fileExtension
            : $".{fileExtension}";

        extension = extension.ToLowerInvariant();

        return extension is ".pdf" or ".docx"
            ? extension
            : throw new ArgumentException("The file extension is unsupported.", nameof(fileExtension));
    }
}
