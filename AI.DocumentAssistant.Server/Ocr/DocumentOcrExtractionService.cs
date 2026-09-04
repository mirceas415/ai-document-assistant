using System.Diagnostics;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Ocr;

public sealed class DocumentOcrExtractionService : IDocumentOcrExtractionService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IPdfPageRenderer _renderer;
    private readonly IOcrService _ocrService;
    private readonly OcrRoutingPolicy _routingPolicy;
    private readonly OcrOptions _configuredOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DocumentOcrExtractionService> _logger;

    public DocumentOcrExtractionService(
        ApplicationDbContext dbContext,
        IPdfPageRenderer renderer,
        IOcrService ocrService,
        OcrRoutingPolicy routingPolicy,
        IOptions<OcrOptions> options,
        TimeProvider timeProvider,
        ILogger<DocumentOcrExtractionService> logger)
    {
        _dbContext = dbContext;
        _renderer = renderer;
        _ocrService = ocrService;
        _routingPolicy = routingPolicy;
        _configuredOptions = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ExtractedTextSection>> ApplyAsync(
        Guid documentId,
        Stream pdfStream,
        IReadOnlyList<ExtractedTextSection> nativeSections,
        bool force,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdfStream);
        ArgumentNullException.ThrowIfNull(nativeSections);

        var technicalAnalysis = await _dbContext.DocumentTechnicalAnalyses
            .AsNoTracking()
            .Include(analysis => analysis.Pages)
            .SingleOrDefaultAsync(
                analysis => analysis.DocumentId == documentId,
                cancellationToken);
        if (technicalAnalysis is null ||
            technicalAnalysis.Status != DocumentTechnicalAnalysisStatus.Ready)
        {
            await PersistRoutingUnavailableAsync(
                documentId,
                technicalAnalysis?.SourceFileHash,
                cancellationToken);
            return Reindex(nativeSections);
        }

        var candidates = _routingPolicy.SelectCandidates(technicalAnalysis.Pages);
        var routingHash = _routingPolicy.ComputeRoutingHash(candidates);
        if (candidates.Count == 0)
        {
            await PersistSkippedAsync(
                documentId,
                technicalAnalysis.SourceFileHash,
                routingHash,
                cancellationToken);
            return Reindex(nativeSections);
        }

        OcrOptions options;
        try
        {
            options = _configuredOptions.ValidatedCopy();
        }
        catch (OcrUnavailableException exception)
        {
            await PersistInfrastructureFailureAsync(
                documentId,
                technicalAnalysis.SourceFileHash,
                candidates,
                routingHash,
                exception.SafeMessage,
                cancellationToken);
            return Combine(nativeSections, candidates, []);
        }

        if (!options.Enabled)
        {
            await PersistInfrastructureFailureAsync(
                documentId,
                technicalAnalysis.SourceFileHash,
                candidates,
                routingHash,
                "Local OCR is disabled.",
                cancellationToken,
                options);
            return Combine(nativeSections, candidates, []);
        }

        OcrEngineInfo engineInfo;
        try
        {
            engineInfo = await _ocrService.GetEngineInfoAsync(
                options.Languages,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Local OCR prerequisites are unavailable for document {DocumentId}. Exception type: {ExceptionType}.",
                documentId,
                exception.GetType().FullName);
            var safeMessage = exception is OcrException ocrException
                ? ocrException.SafeMessage
                : OcrArchitecture.UnavailableMessage;
            await PersistInfrastructureFailureAsync(
                documentId,
                technicalAnalysis.SourceFileHash,
                candidates,
                routingHash,
                safeMessage,
                cancellationToken,
                options);
            return Combine(nativeSections, candidates, []);
        }

        var configurationHash = OcrArchitecture.ComputeConfigurationHash(
            options,
            engineInfo,
            options.Languages);
        if (!force)
        {
            var reusedSections = await TryLoadReusableSectionsAsync(
                documentId,
                technicalAnalysis.SourceFileHash,
                candidates,
                routingHash,
                configurationHash,
                cancellationToken);
            if (reusedSections is not null)
            {
                return Combine(nativeSections, candidates, reusedSections);
            }
        }

        var analysis = await BeginAttemptAsync(
            documentId,
            technicalAnalysis.SourceFileHash,
            candidates.Count,
            routingHash,
            configurationHash,
            options,
            engineInfo,
            cancellationToken);
        var selectedCandidates = candidates.Take(options.MaxCandidatePages).ToArray();
        var firstLimitedCandidate = candidates.Skip(options.MaxCandidatePages).FirstOrDefault();
        var diagnosticCapacity = selectedCandidates.Length + (firstLimitedCandidate is null ? 0 : 1);
        var pageResults = new List<DocumentPageOcrResult>(diagnosticCapacity);
        var extractedOcrSections = new List<ExtractedTextSection>();

        try
        {
            if (firstLimitedCandidate is not null)
            {
                pageResults.Add(PageFailure(
                    documentId,
                    firstLimitedCandidate.PageNumber,
                    DocumentPageOcrStatus.SkippedLimit,
                    "The configured OCR candidate-page limit was exceeded."));
            }

            var infrastructureUnavailable = false;
            foreach (var candidate in selectedCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (infrastructureUnavailable)
                {
                    pageResults.Add(PageFailure(
                        documentId,
                        candidate.PageNumber,
                        DocumentPageOcrStatus.Failed,
                        OcrArchitecture.UnavailableMessage));
                    continue;
                }

                var stopwatch = Stopwatch.StartNew();
                try
                {
                    using var image = await _renderer.RenderPageAsync(
                        pdfStream,
                        candidate.PageNumber,
                        options.RenderDpi,
                        options.MaxRenderedPixels,
                        cancellationToken);
                    var result = await _ocrService.OcrPageAsync(
                        image,
                        options.Languages,
                        cancellationToken);
                    stopwatch.Stop();

                    var content = result.Text.Trim();
                    if (content.Length == 0)
                    {
                        pageResults.Add(new DocumentPageOcrResult
                        {
                            DocumentOcrAnalysisId = documentId,
                            PageNumber = candidate.PageNumber,
                            Status = DocumentPageOcrStatus.Empty,
                            SourceTechnicalType = TechnicalType.Scanned,
                            MeanConfidence = NormalizeConfidence(result.MeanConfidence),
                            EffectiveRenderDpi = image.EffectiveDpi,
                            RenderedWidthPixels = image.WidthPixels,
                            RenderedHeightPixels = image.HeightPixels,
                            ProcessingDurationMs = stopwatch.ElapsedMilliseconds,
                            UsedInExtraction = false
                        });
                        continue;
                    }

                    extractedOcrSections.Add(new ExtractedTextSection(
                        0,
                        content,
                        candidate.PageNumber,
                        ExtractionMethod: DocumentTextExtractionMethod.Ocr));
                    pageResults.Add(new DocumentPageOcrResult
                    {
                        DocumentOcrAnalysisId = documentId,
                        PageNumber = candidate.PageNumber,
                        Status = DocumentPageOcrStatus.Ready,
                        SourceTechnicalType = TechnicalType.Scanned,
                        RecognizedCharacterCount = content.Length,
                        RecognizedWordCount = CountWords(content),
                        MeanConfidence = NormalizeConfidence(result.MeanConfidence),
                        EffectiveRenderDpi = image.EffectiveDpi,
                        RenderedWidthPixels = image.WidthPixels,
                        RenderedHeightPixels = image.HeightPixels,
                        ProcessingDurationMs = stopwatch.ElapsedMilliseconds,
                        UsedInExtraction = true
                    });
                    analysis.EngineName = Truncate(
                        result.EngineName,
                        OcrArchitecture.MaximumEngineNameLength);
                    analysis.EngineVersion = Truncate(
                        result.EngineVersion,
                        OcrArchitecture.MaximumEngineVersionLength);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    stopwatch.Stop();
                    var safeMessage = exception is OcrException ocrException
                        ? ocrException.SafeMessage
                        : OcrArchitecture.FailedMessage;
                    pageResults.Add(PageFailure(
                        documentId,
                        candidate.PageNumber,
                        DocumentPageOcrStatus.Failed,
                        safeMessage,
                        stopwatch.ElapsedMilliseconds));
                    infrastructureUnavailable = exception is OcrUnavailableException;
                    _logger.LogWarning(
                        "Local OCR failed for document {DocumentId}, page {PageNumber}. Exception type: {ExceptionType}. Document content was omitted.",
                        documentId,
                        candidate.PageNumber,
                        exception.GetType().FullName);
                }
            }

            CompleteAnalysis(analysis, pageResults, options.MaxCandidatePages);
            _dbContext.DocumentPageOcrResults.AddRange(pageResults);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            foreach (var candidate in selectedCandidates.Where(candidate =>
                         pageResults.All(result => result.PageNumber != candidate.PageNumber)))
            {
                pageResults.Add(PageFailure(
                    documentId,
                    candidate.PageNumber,
                    DocumentPageOcrStatus.Failed,
                    OcrArchitecture.InterruptedMessage));
            }

            analysis.Status = OcrArchitecture.GetCompletedStatus(
                candidates.Count,
                pageResults.Count(result => result.Status == DocumentPageOcrStatus.Ready));
            analysis.SuccessfulPageCount = pageResults.Count(result =>
                result.Status == DocumentPageOcrStatus.Ready);
            analysis.FailedPageCount = candidates.Count - analysis.SuccessfulPageCount;
            analysis.ProcessedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            analysis.LastError = OcrArchitecture.InterruptedMessage;
            _dbContext.DocumentPageOcrResults.AddRange(pageResults);
            await _dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        return Combine(nativeSections, candidates, extractedOcrSections);
    }

    private async Task<IReadOnlyList<ExtractedTextSection>?> TryLoadReusableSectionsAsync(
        Guid documentId,
        string? sourceFileHash,
        IReadOnlyList<DocumentPageTechnicalAnalysis> candidates,
        string routingHash,
        string configurationHash,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.DocumentOcrAnalyses
            .AsNoTracking()
            .Include(analysis => analysis.Pages)
            .SingleOrDefaultAsync(
                analysis => analysis.DocumentId == documentId,
                cancellationToken);
        if (existing is null ||
            existing.Status != DocumentOcrStatus.Ready ||
            existing.CandidatePageCount != candidates.Count ||
            existing.SuccessfulPageCount != candidates.Count ||
            !string.Equals(existing.SourceFileHash, sourceFileHash, StringComparison.Ordinal) ||
            !string.Equals(existing.RoutingVersion, _routingPolicy.Version, StringComparison.Ordinal) ||
            !string.Equals(existing.RoutingHash, routingHash, StringComparison.Ordinal) ||
            !string.Equals(existing.ConfigurationHash, configurationHash, StringComparison.Ordinal) ||
            existing.Pages.Count != candidates.Count ||
            existing.Pages.Any(page =>
                page.Status != DocumentPageOcrStatus.Ready ||
                !page.UsedInExtraction))
        {
            return null;
        }

        var candidateNumbers = candidates.Select(page => page.PageNumber).ToArray();
        var sections = await _dbContext.DocumentTextSections
            .AsNoTracking()
            .Where(section =>
                section.DocumentId == documentId &&
                section.ExtractionMethod == DocumentTextExtractionMethod.Ocr &&
                section.PageNumber != null &&
                candidateNumbers.Contains(section.PageNumber.Value))
            .OrderBy(section => section.SectionIndex)
            .Select(section => new ExtractedTextSection(
                section.SectionIndex,
                section.Content,
                section.PageNumber,
                section.SectionTitle,
                DocumentTextExtractionMethod.Ocr))
            .ToListAsync(cancellationToken);

        return sections.Count == candidates.Count &&
               sections.Select(section => section.PageNumber!.Value)
                   .OrderBy(pageNumber => pageNumber)
                   .SequenceEqual(candidateNumbers.OrderBy(pageNumber => pageNumber))
            ? sections
            : null;
    }

    private async Task<DocumentOcrAnalysis> BeginAttemptAsync(
        Guid documentId,
        string? sourceFileHash,
        int candidatePageCount,
        string routingHash,
        string configurationHash,
        OcrOptions options,
        OcrEngineInfo engineInfo,
        CancellationToken cancellationToken)
    {
        var analysis = await GetOrCreateAnalysisAsync(documentId, cancellationToken);
        await DeletePageResultsAsync(documentId, cancellationToken);
        ResetAnalysis(analysis);
        analysis.Status = DocumentOcrStatus.Processing;
        analysis.CandidatePageCount = candidatePageCount;
        analysis.EngineName = Truncate(
            engineInfo.EngineName,
            OcrArchitecture.MaximumEngineNameLength);
        analysis.EngineVersion = Truncate(
            engineInfo.EngineVersion,
            OcrArchitecture.MaximumEngineVersionLength);
        analysis.Languages = options.Languages;
        analysis.RenderDpi = options.RenderDpi;
        analysis.MaxCandidatePages = options.MaxCandidatePages;
        analysis.MaxRenderedPixels = options.MaxRenderedPixels;
        analysis.SourceFileHash = sourceFileHash;
        analysis.RoutingVersion = _routingPolicy.Version;
        analysis.RoutingHash = routingHash;
        analysis.ConfigurationHash = configurationHash;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return analysis;
    }

    private async Task PersistSkippedAsync(
        Guid documentId,
        string? sourceFileHash,
        string routingHash,
        CancellationToken cancellationToken)
    {
        var analysis = await GetOrCreateAnalysisAsync(documentId, cancellationToken);
        await DeletePageResultsAsync(documentId, cancellationToken);
        ResetAnalysis(analysis);
        analysis.Status = DocumentOcrStatus.Skipped;
        analysis.SourceFileHash = sourceFileHash;
        analysis.RoutingVersion = _routingPolicy.Version;
        analysis.RoutingHash = routingHash;
        analysis.ProcessedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistRoutingUnavailableAsync(
        Guid documentId,
        string? sourceFileHash,
        CancellationToken cancellationToken)
    {
        var analysis = await GetOrCreateAnalysisAsync(documentId, cancellationToken);
        await DeletePageResultsAsync(documentId, cancellationToken);
        ResetAnalysis(analysis);
        analysis.Status = DocumentOcrStatus.NotAnalyzed;
        analysis.SourceFileHash = sourceFileHash;
        analysis.RoutingVersion = _routingPolicy.Version;
        analysis.LastError = OcrArchitecture.RoutingUnavailableMessage;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistInfrastructureFailureAsync(
        Guid documentId,
        string? sourceFileHash,
        IReadOnlyList<DocumentPageTechnicalAnalysis> candidates,
        string routingHash,
        string safeMessage,
        CancellationToken cancellationToken,
        OcrOptions? options = null)
    {
        var analysis = await GetOrCreateAnalysisAsync(documentId, cancellationToken);
        await DeletePageResultsAsync(documentId, cancellationToken);
        ResetAnalysis(analysis);
        analysis.Status = DocumentOcrStatus.Failed;
        analysis.CandidatePageCount = candidates.Count;
        analysis.FailedPageCount = candidates.Count;
        analysis.EngineName = Truncate(
            _ocrService.EngineName,
            OcrArchitecture.MaximumEngineNameLength);
        analysis.EngineVersion = Truncate(
            _ocrService.EngineVersion,
            OcrArchitecture.MaximumEngineVersionLength);
        analysis.Languages = Truncate(
            options?.Languages ?? _configuredOptions.Languages,
            OcrArchitecture.MaximumLanguagesLength);
        analysis.RenderDpi = options?.RenderDpi;
        analysis.MaxCandidatePages = options?.MaxCandidatePages;
        analysis.MaxRenderedPixels = options?.MaxRenderedPixels;
        analysis.SourceFileHash = sourceFileHash;
        analysis.RoutingVersion = _routingPolicy.Version;
        analysis.RoutingHash = routingHash;
        if (options is not null)
        {
            analysis.ConfigurationHash = OcrArchitecture.ComputeUnavailableConfigurationHash(
                options,
                _ocrService.EngineName,
                _ocrService.EngineVersion,
                options.Languages);
        }

        analysis.ProcessedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        analysis.LastError = Truncate(safeMessage, OcrArchitecture.MaximumErrorLength);
        var diagnosticLimit = options?.MaxCandidatePages ??
            Math.Clamp(_configuredOptions.MaxCandidatePages, 1, 1_000);
        var pageResults = candidates.Take(diagnosticLimit)
            .Select(candidate => PageFailure(
                documentId,
                candidate.PageNumber,
                DocumentPageOcrStatus.Failed,
                safeMessage))
            .ToList();
        var firstLimitedCandidate = candidates.Skip(diagnosticLimit).FirstOrDefault();
        if (firstLimitedCandidate is not null)
        {
            pageResults.Add(PageFailure(
                documentId,
                firstLimitedCandidate.PageNumber,
                DocumentPageOcrStatus.SkippedLimit,
                "The configured OCR candidate-page limit was exceeded."));
        }

        _dbContext.DocumentPageOcrResults.AddRange(pageResults);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<DocumentOcrAnalysis> GetOrCreateAnalysisAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var analysis = await _dbContext.DocumentOcrAnalyses
            .SingleOrDefaultAsync(
                item => item.DocumentId == documentId,
                cancellationToken);
        if (analysis is not null)
        {
            return analysis;
        }

        analysis = new DocumentOcrAnalysis { DocumentId = documentId };
        _dbContext.DocumentOcrAnalyses.Add(analysis);
        return analysis;
    }

    private async Task DeletePageResultsAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.DocumentPageOcrResults
            .Where(page => page.DocumentOcrAnalysisId == documentId);
        if (_dbContext.Database.IsRelational())
        {
            await query.ExecuteDeleteAsync(cancellationToken);
            return;
        }

        _dbContext.DocumentPageOcrResults.RemoveRange(
            await query.ToListAsync(cancellationToken));
    }

    private void CompleteAnalysis(
        DocumentOcrAnalysis analysis,
        IReadOnlyList<DocumentPageOcrResult> pageResults,
        int maximumCandidatePages)
    {
        analysis.SuccessfulPageCount = pageResults.Count(page =>
            page.Status == DocumentPageOcrStatus.Ready);
        analysis.FailedPageCount = analysis.CandidatePageCount - analysis.SuccessfulPageCount;
        analysis.Status = OcrArchitecture.GetCompletedStatus(
            analysis.CandidatePageCount,
            analysis.SuccessfulPageCount);
        analysis.ProcessedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        analysis.LastError = analysis.Status switch
        {
            DocumentOcrStatus.Ready => null,
            _ when analysis.CandidatePageCount > maximumCandidatePages =>
                Truncate(
                    $"Only the first {maximumCandidatePages} scanned pages were eligible for OCR because the configured page limit was reached.",
                    OcrArchitecture.MaximumErrorLength),
            DocumentOcrStatus.Partial =>
                "Some scanned pages could not be processed by local OCR.",
            _ => OcrArchitecture.FailedMessage
        };
    }

    private static DocumentPageOcrResult PageFailure(
        Guid documentId,
        int pageNumber,
        DocumentPageOcrStatus status,
        string safeMessage,
        long? durationMs = null) =>
        new()
        {
            DocumentOcrAnalysisId = documentId,
            PageNumber = pageNumber,
            Status = status,
            SourceTechnicalType = TechnicalType.Scanned,
            ProcessingDurationMs = durationMs,
            UsedInExtraction = false,
            LastError = Truncate(safeMessage, OcrArchitecture.MaximumErrorLength)
        };

    private static IReadOnlyList<ExtractedTextSection> Combine(
        IReadOnlyList<ExtractedTextSection> nativeSections,
        IReadOnlyList<DocumentPageTechnicalAnalysis> candidates,
        IReadOnlyList<ExtractedTextSection> ocrSections)
    {
        var candidatePages = candidates.Select(page => page.PageNumber).ToHashSet();
        return Reindex(nativeSections
            .Where(section =>
                section.PageNumber is null ||
                !candidatePages.Contains(section.PageNumber.Value))
            .Concat(ocrSections)
            .OrderBy(section => section.PageNumber ?? int.MaxValue)
            .ThenBy(section => section.SectionIndex)
            .ToArray());
    }

    private static IReadOnlyList<ExtractedTextSection> Reindex(
        IEnumerable<ExtractedTextSection> sections) =>
        sections.Select((section, index) => section with { SectionIndex = index }).ToArray();

    private static int CountWords(string text)
    {
        var count = 0;
        var inWord = false;
        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (!inWord)
                {
                    count++;
                    inWord = true;
                }
            }
            else
            {
                inWord = false;
            }
        }

        return count;
    }

    private static double? NormalizeConfidence(double? value) =>
        value is not null && double.IsFinite(value.Value)
            ? Math.Clamp(value.Value, 0d, 1d)
            : null;

    private static void ResetAnalysis(DocumentOcrAnalysis analysis)
    {
        analysis.Status = DocumentOcrStatus.NotAnalyzed;
        analysis.CandidatePageCount = 0;
        analysis.SuccessfulPageCount = 0;
        analysis.FailedPageCount = 0;
        analysis.EngineName = null;
        analysis.EngineVersion = null;
        analysis.Languages = null;
        analysis.RenderDpi = null;
        analysis.MaxCandidatePages = null;
        analysis.MaxRenderedPixels = null;
        analysis.SourceFileHash = null;
        analysis.RoutingVersion = null;
        analysis.RoutingHash = null;
        analysis.ConfigurationHash = null;
        analysis.ProcessedAtUtc = null;
        analysis.LastError = null;
    }

    private static string? Truncate(string? value, int maximumLength) =>
        value is null || value.Length <= maximumLength
            ? value
            : value[..maximumLength];
}
