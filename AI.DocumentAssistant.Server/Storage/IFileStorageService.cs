namespace AI.DocumentAssistant.Server.Storage;

public interface IFileStorageService
{
    Task<string> SaveAsync(
        Stream source,
        string fileExtension,
        CancellationToken cancellationToken);

    Task DeleteAsync(string storedFileName, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string storedFileName, CancellationToken cancellationToken);
}
