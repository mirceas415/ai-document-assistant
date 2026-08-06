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
    string? ProcessingError,
    int ChunkCount,
    DateTime? ChunkedAtUtc,
    string? ChunkingError);

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
    string? ProcessingError,
    int ChunkCount,
    DateTime? ChunkedAtUtc,
    string? ChunkingError);

public sealed record ExtractedTextSectionResponse(
    int SectionIndex,
    int? PageNumber,
    string? SectionTitle,
    string Content);

public sealed record DocumentChunkResponse(
    int ChunkIndex,
    string Content,
    int TokenCount,
    int CharacterCount,
    int? PageStart,
    int? PageEnd,
    string? SectionTitle,
    int SourceSectionStartIndex,
    int SourceSectionEndIndex);

public sealed class UploadDocumentRequest
{
    public IFormFile? File { get; init; }
}
