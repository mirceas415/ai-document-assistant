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
    DateTime UpdatedAtUtc,
    DateTime? ProcessingStartedAtUtc,
    DateTime? ProcessedAtUtc,
    int ExtractedSectionCount,
    long ExtractedCharacterCount,
    string? ProcessingError);

public sealed record DocumentDetails(
    Guid Id,
    Guid ProjectId,
    string OriginalFileName,
    string ContentType,
    long FileSizeBytes,
    DocumentStatus Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? ProcessingStartedAtUtc,
    DateTime? ProcessedAtUtc,
    int ExtractedSectionCount,
    long ExtractedCharacterCount,
    string? ProcessingError);

public sealed record ExtractedTextSectionResponse(
    int SectionIndex,
    int? PageNumber,
    string? SectionTitle,
    string Content);

public sealed class UploadDocumentRequest
{
    public IFormFile? File { get; init; }
}
