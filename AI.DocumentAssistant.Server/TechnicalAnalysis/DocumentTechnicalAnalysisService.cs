using System.Security.Cryptography;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AI.DocumentAssistant.Server.TechnicalAnalysis;

public sealed class DocumentTechnicalAnalysisService
    : IDocumentTechnicalAnalysisService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IFileStorageService _fileStorage;
    private readonly IPdfTechnicalAnalyzer _pdfAnalyzer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DocumentTechnicalAnalysisService> _logger;

    public DocumentTechnicalAnalysisService(
        ApplicationDbContext dbContext,
        IFileStorageService fileStorage,
        IPdfTechnicalAnalyzer pdfAnalyzer,
        TimeProvider timeProvider,
        ILogger<DocumentTechnicalAnalysisService> logger)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _pdfAnalyzer = pdfAnalyzer;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<DocumentTechnicalAnalysisRunResult> AnalyzeAsync(
        Guid documentId,
        bool force,
        CancellationToken cancellationToken)
    {
        var document = await _dbContext.Documents
            .AsNoTracking()
            .Where(item => item.Id == documentId)
            .Select(item => new
            {
                item.Id,
                item.ContentType,
                item.StoredFileName
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (document is null)
        {
            throw new PdfTechnicalAnalysisException(
                "The document is no longer available.");
        }

        if (!PdfTechnicalAnalysisArchitecture.IsPdf(document.ContentType))
        {
            return await PersistSkippedAsync(document.Id, cancellationToken);
        }

        string sourceFileHash;
        try
        {
            sourceFileHash = await ComputeSourceFileHashAsync(
                document.StoredFileName,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await TryPersistFailedAsync(
                document.Id,
                null,
                "Technical PDF analysis was interrupted. Please retry.");
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Could not hash the source file for technical analysis of document {DocumentId}. Exception type: {ExceptionType}.",
                document.Id,
                exception.GetType().FullName);
            await TryPersistFailedAsync(
                document.Id,
                null,
                PdfTechnicalAnalysisArchitecture.SafeFailureMessage);
            throw new PdfTechnicalAnalysisException(
                PdfTechnicalAnalysisArchitecture.SafeFailureMessage,
                exception);
        }

        var existing = await _dbContext.DocumentTechnicalAnalyses
            .AsNoTracking()
            .SingleOrDefaultAsync(
                analysis => analysis.DocumentId == document.Id,
                cancellationToken);
        if (!force &&
            existing is not null &&
            existing.Status == DocumentTechnicalAnalysisStatus.Ready &&
            MatchesVersion(existing, sourceFileHash, _pdfAnalyzer.AnalyzerVersion))
        {
            return new DocumentTechnicalAnalysisRunResult(
                existing.Status,
                existing.TechnicalType,
                sourceFileHash,
                _pdfAnalyzer.AnalyzerVersion,
                true);
        }

        await ClaimProcessingAsync(
            document.Id,
            sourceFileHash,
            _pdfAnalyzer.AnalyzerVersion,
            existing,
            cancellationToken);

        try
        {
            await using var pdfStream = await _fileStorage.OpenReadAsync(
                document.StoredFileName,
                cancellationToken);
            var result = await _pdfAnalyzer.AnalyzeAsync(
                pdfStream,
                cancellationToken);

            await PersistSuccessAsync(
                document.Id,
                sourceFileHash,
                _pdfAnalyzer.AnalyzerVersion,
                result,
                cancellationToken);

            return new DocumentTechnicalAnalysisRunResult(
                DocumentTechnicalAnalysisStatus.Ready,
                result.TechnicalType,
                sourceFileHash,
                _pdfAnalyzer.AnalyzerVersion,
                false);
        }
        catch (OperationCanceledException)
        {
            await TryPersistFailedAsync(
                document.Id,
                sourceFileHash,
                "Technical PDF analysis was interrupted. Please retry.");
            throw;
        }
        catch (PdfTechnicalAnalysisException exception)
        {
            await TryPersistFailedAsync(
                document.Id,
                sourceFileHash,
                exception.SafeMessage);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Technical PDF analysis failed for document {DocumentId}. Exception type: {ExceptionType}. Document content was omitted.",
                document.Id,
                exception.GetType().FullName);
            await TryPersistFailedAsync(
                document.Id,
                sourceFileHash,
                PdfTechnicalAnalysisArchitecture.SafeFailureMessage);
            throw new PdfTechnicalAnalysisException(
                PdfTechnicalAnalysisArchitecture.SafeFailureMessage,
                exception);
        }
    }

    private async Task<DocumentTechnicalAnalysisRunResult> PersistSkippedAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var analysis = await _dbContext.DocumentTechnicalAnalyses
            .SingleOrDefaultAsync(
                item => item.DocumentId == documentId,
                cancellationToken);
        if (analysis is not null &&
            analysis.Status == DocumentTechnicalAnalysisStatus.Skipped &&
            string.Equals(
                analysis.AnalyzerVersion,
                _pdfAnalyzer.AnalyzerVersion,
                StringComparison.Ordinal))
        {
            return new DocumentTechnicalAnalysisRunResult(
                analysis.Status,
                analysis.TechnicalType,
                null,
                analysis.AnalyzerVersion,
                true);
        }

        if (analysis is null)
        {
            analysis = new DocumentTechnicalAnalysis { DocumentId = documentId };
            _dbContext.DocumentTechnicalAnalyses.Add(analysis);
        }

        await DeletePagesAsync(documentId, cancellationToken);
        ResetResult(analysis);
        analysis.Status = DocumentTechnicalAnalysisStatus.Skipped;
        analysis.AnalyzerVersion = _pdfAnalyzer.AnalyzerVersion;
        analysis.AnalyzedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new DocumentTechnicalAnalysisRunResult(
            analysis.Status,
            analysis.TechnicalType,
            null,
            analysis.AnalyzerVersion,
            false);
    }

    private async Task<string> ComputeSourceFileHashAsync(
        string storedFileName,
        CancellationToken cancellationToken)
    {
        await using var source = await _fileStorage.OpenReadAsync(
            storedFileName,
            cancellationToken);
        var hash = await SHA256.HashDataAsync(source, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private async Task ClaimProcessingAsync(
        Guid documentId,
        string sourceFileHash,
        string analyzerVersion,
        DocumentTechnicalAnalysis? existing,
        CancellationToken cancellationToken)
    {
        if (existing is null)
        {
            var claim = new DocumentTechnicalAnalysis
            {
                DocumentId = documentId,
                Status = DocumentTechnicalAnalysisStatus.Processing,
                TechnicalType = TechnicalType.Unknown,
                SourceFileHash = sourceFileHash,
                AnalyzerVersion = analyzerVersion
            };
            _dbContext.DocumentTechnicalAnalyses.Add(claim);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException exception)
            {
                _dbContext.Entry(claim).State = EntityState.Detached;
                throw new PdfTechnicalAnalysisException(
                    "Technical PDF analysis is already processing.",
                    exception);
            }
        }

        if (_dbContext.Database.IsRelational())
        {
            var updated = await _dbContext.DocumentTechnicalAnalyses
                .Where(analysis =>
                    analysis.DocumentId == documentId &&
                    analysis.Status != DocumentTechnicalAnalysisStatus.Processing)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(
                        analysis => analysis.Status,
                        DocumentTechnicalAnalysisStatus.Processing)
                    .SetProperty(
                        analysis => analysis.TechnicalType,
                        TechnicalType.Unknown)
                    .SetProperty(analysis => analysis.PageCount, 0)
                    .SetProperty(analysis => analysis.TextBasedPageCount, 0)
                    .SetProperty(analysis => analysis.ScannedPageCount, 0)
                    .SetProperty(analysis => analysis.ImageBasedPageCount, 0)
                    .SetProperty(analysis => analysis.MixedPageCount, 0)
                    .SetProperty(analysis => analysis.UnknownPageCount, 0)
                    .SetProperty(analysis => analysis.SourceFileHash, sourceFileHash)
                    .SetProperty(analysis => analysis.AnalyzerVersion, analyzerVersion)
                    .SetProperty(analysis => analysis.AnalyzedAtUtc, (DateTime?)null)
                    .SetProperty(analysis => analysis.LastError, (string?)null),
                    cancellationToken);
            if (updated != 1)
            {
                throw new PdfTechnicalAnalysisException(
                    "Technical PDF analysis is already processing.");
            }

            var locallyTracked = _dbContext.DocumentTechnicalAnalyses.Local
                .SingleOrDefault(analysis => analysis.DocumentId == documentId);
            if (locallyTracked is not null)
            {
                _dbContext.Entry(locallyTracked).State = EntityState.Detached;
            }

            return;
        }

        var tracked = await _dbContext.DocumentTechnicalAnalyses
            .SingleAsync(
                analysis => analysis.DocumentId == documentId,
                cancellationToken);
        if (tracked.Status == DocumentTechnicalAnalysisStatus.Processing)
        {
            throw new PdfTechnicalAnalysisException(
                "Technical PDF analysis is already processing.");
        }

        ResetResult(tracked);
        tracked.Status = DocumentTechnicalAnalysisStatus.Processing;
        tracked.SourceFileHash = sourceFileHash;
        tracked.AnalyzerVersion = analyzerVersion;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistSuccessAsync(
        Guid documentId,
        string sourceFileHash,
        string analyzerVersion,
        PdfTechnicalAnalysisResult result,
        CancellationToken cancellationToken)
    {
        ValidateResult(result);

        await using var transaction = await BeginTransactionIfSupportedAsync(
            cancellationToken);
        var analysis = await LoadCurrentAttemptAsync(
            documentId,
            sourceFileHash,
            analyzerVersion,
            cancellationToken);
        await DeletePagesAsync(documentId, cancellationToken);

        var pages = result.Pages.Select(page => new DocumentPageTechnicalAnalysis
        {
            DocumentTechnicalAnalysisId = documentId,
            PageNumber = page.PageNumber,
            TechnicalType = page.TechnicalType,
            TextCharacterCount = page.TextCharacterCount,
            WordCount = page.WordCount,
            ImageCount = page.ImageCount,
            ImageCoverageRatio = page.ImageCoverageRatio,
            HasMeaningfulText = page.HasMeaningfulText,
            HasPageSizedImage = page.HasPageSizedImage
        }).ToArray();
        _dbContext.DocumentPageTechnicalAnalyses.AddRange(pages);

        analysis.Status = DocumentTechnicalAnalysisStatus.Ready;
        analysis.TechnicalType = result.TechnicalType;
        analysis.PageCount = pages.Length;
        analysis.TextBasedPageCount = pages.Count(page =>
            page.TechnicalType == TechnicalType.TextBased);
        analysis.ScannedPageCount = pages.Count(page =>
            page.TechnicalType == TechnicalType.Scanned);
        analysis.ImageBasedPageCount = pages.Count(page =>
            page.TechnicalType == TechnicalType.ImageBased);
        analysis.MixedPageCount = pages.Count(page =>
            page.TechnicalType == TechnicalType.Mixed);
        analysis.UnknownPageCount = pages.Count(page =>
            page.TechnicalType == TechnicalType.Unknown);
        analysis.SourceFileHash = sourceFileHash;
        analysis.AnalyzerVersion = analyzerVersion;
        analysis.AnalyzedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        analysis.LastError = null;

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private async Task<DocumentTechnicalAnalysis> LoadCurrentAttemptAsync(
        Guid documentId,
        string sourceFileHash,
        string analyzerVersion,
        CancellationToken cancellationToken)
    {
        var analysis = await _dbContext.DocumentTechnicalAnalyses
            .SingleOrDefaultAsync(
                item => item.DocumentId == documentId,
                cancellationToken);
        if (analysis is null ||
            analysis.Status != DocumentTechnicalAnalysisStatus.Processing ||
            !MatchesVersion(analysis, sourceFileHash, analyzerVersion))
        {
            throw new PdfTechnicalAnalysisException(
                "Technical PDF analysis changed while it was running.");
        }

        return analysis;
    }

    private async Task TryPersistFailedAsync(
        Guid documentId,
        string? sourceFileHash,
        string safeMessage)
    {
        try
        {
            var analysis = await _dbContext.DocumentTechnicalAnalyses
                .SingleOrDefaultAsync(item => item.DocumentId == documentId);
            if (analysis is null)
            {
                analysis = new DocumentTechnicalAnalysis { DocumentId = documentId };
                _dbContext.DocumentTechnicalAnalyses.Add(analysis);
            }
            else if (analysis.Status == DocumentTechnicalAnalysisStatus.Processing &&
                (sourceFileHash is null ||
                 !MatchesVersion(
                     analysis,
                     sourceFileHash,
                     _pdfAnalyzer.AnalyzerVersion)))
            {
                return;
            }

            await DeletePagesAsync(documentId, CancellationToken.None);
            ResetResult(analysis);
            analysis.Status = DocumentTechnicalAnalysisStatus.Failed;
            analysis.SourceFileHash = sourceFileHash;
            analysis.AnalyzerVersion = _pdfAnalyzer.AnalyzerVersion;
            analysis.LastError = Truncate(safeMessage);
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Could not persist Failed technical-analysis state for document {DocumentId}.",
                documentId);
        }
    }

    private async Task DeletePagesAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.DocumentPageTechnicalAnalyses
            .Where(page => page.DocumentTechnicalAnalysisId == documentId);
        if (_dbContext.Database.IsRelational())
        {
            await query.ExecuteDeleteAsync(cancellationToken);
            return;
        }

        _dbContext.DocumentPageTechnicalAnalyses.RemoveRange(
            await query.ToListAsync(cancellationToken));
    }

    private Task<IDbContextTransaction?> BeginTransactionIfSupportedAsync(
        CancellationToken cancellationToken) =>
        _dbContext.Database.IsRelational()
            ? BeginRelationalTransactionAsync(cancellationToken)
            : Task.FromResult<IDbContextTransaction?>(null);

    private async Task<IDbContextTransaction?> BeginRelationalTransactionAsync(
        CancellationToken cancellationToken) =>
        await _dbContext.Database.BeginTransactionAsync(cancellationToken);

    private static void ValidateResult(PdfTechnicalAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var expectedPageNumber = 1;
        foreach (var page in result.Pages.OrderBy(page => page.PageNumber))
        {
            if (page.PageNumber != expectedPageNumber ||
                page.ImageCoverageRatio is < 0 or > 1 ||
                !double.IsFinite(page.ImageCoverageRatio))
            {
                throw new PdfTechnicalAnalysisException(
                    PdfTechnicalAnalysisArchitecture.SafeFailureMessage);
            }

            expectedPageNumber++;
        }

        if (PdfTechnicalClassifier.ClassifyDocument(result.Pages) !=
            result.TechnicalType)
        {
            throw new PdfTechnicalAnalysisException(
                PdfTechnicalAnalysisArchitecture.SafeFailureMessage);
        }
    }

    private static void ResetResult(DocumentTechnicalAnalysis analysis)
    {
        analysis.TechnicalType = TechnicalType.Unknown;
        analysis.PageCount = 0;
        analysis.TextBasedPageCount = 0;
        analysis.ScannedPageCount = 0;
        analysis.ImageBasedPageCount = 0;
        analysis.MixedPageCount = 0;
        analysis.UnknownPageCount = 0;
        analysis.SourceFileHash = null;
        analysis.AnalyzedAtUtc = null;
        analysis.LastError = null;
    }

    private static bool MatchesVersion(
        DocumentTechnicalAnalysis analysis,
        string sourceFileHash,
        string analyzerVersion) =>
        string.Equals(
            analysis.SourceFileHash,
            sourceFileHash,
            StringComparison.Ordinal) &&
        string.Equals(
            analysis.AnalyzerVersion,
            analyzerVersion,
            StringComparison.Ordinal);

    private static string Truncate(string value) =>
        value.Length <= PdfTechnicalAnalysisArchitecture.MaximumErrorLength
            ? value
            : value[..PdfTechnicalAnalysisArchitecture.MaximumErrorLength];
}
