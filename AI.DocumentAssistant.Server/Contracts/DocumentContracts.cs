using AI.DocumentAssistant.Server.Models;
using Microsoft.AspNetCore.Http;

namespace AI.DocumentAssistant.Server.Contracts;

public sealed record DocumentSummary(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    long FileSizeBytes,
    DocumentStatus Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record DocumentDetails(
    Guid Id,
    Guid ProjectId,
    string OriginalFileName,
    string ContentType,
    long FileSizeBytes,
    DocumentStatus Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed class UploadDocumentRequest
{
    public IFormFile? File { get; init; }
}
