using System.Security.Cryptography;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Storage;
using AI.DocumentAssistant.Server.TechnicalAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StoredDocument = AI.DocumentAssistant.Server.Models.Document;

namespace AI.DocumentAssistant.Server.Tests;

public sealed class DocumentTechnicalAnalysisServiceTests
{
    [Fact]
    public async Task MatchingSourceHashAndVersionReuseReadyAnalysis()
    {
        await using var fixture = await ServiceFixture.CreateAsync();

        var first = await fixture.Service.AnalyzeAsync(
            fixture.DocumentId,
            force: false,
            CancellationToken.None);
        var second = await fixture.Service.AnalyzeAsync(
            fixture.DocumentId,
            force: false,
            CancellationToken.None);

        var expectedHash = Convert.ToHexString(SHA256.HashData(fixture.Storage.Bytes));
        Assert.False(first.Reused);
        Assert.True(second.Reused);
        Assert.Equal(expectedHash, second.SourceFileHash);
        Assert.Equal(1, fixture.Analyzer.CallCount);
        Assert.Equal(3, fixture.Storage.OpenCount);

        fixture.Context.ChangeTracker.Clear();
        var persisted = await fixture.Context.DocumentTechnicalAnalyses
            .Include(analysis => analysis.Pages)
            .SingleAsync();
        Assert.Equal(DocumentTechnicalAnalysisStatus.Ready, persisted.Status);
        Assert.Equal(TechnicalType.TextBased, persisted.TechnicalType);
        Assert.Equal("test-analyzer-v1", persisted.AnalyzerVersion);
        Assert.Equal(expectedHash, persisted.SourceFileHash);
        Assert.Single(persisted.Pages);
    }

    [Fact]
    public async Task ForcedRebuildAlwaysRunsAnalyzerAndReplacesPages()
    {
        await using var fixture = await ServiceFixture.CreateAsync();
        await fixture.Service.AnalyzeAsync(
            fixture.DocumentId,
            force: false,
            CancellationToken.None);

        fixture.Analyzer.Result = Result(TechnicalType.Scanned, coverage: 0.96);
        var rebuilt = await fixture.Service.AnalyzeAsync(
            fixture.DocumentId,
            force: true,
            CancellationToken.None);

        Assert.False(rebuilt.Reused);
        Assert.Equal(2, fixture.Analyzer.CallCount);
        fixture.Context.ChangeTracker.Clear();
        var analysis = await fixture.Context.DocumentTechnicalAnalyses
            .Include(item => item.Pages)
            .SingleAsync();
        Assert.Equal(TechnicalType.Scanned, analysis.TechnicalType);
        Assert.Equal(1, analysis.ScannedPageCount);
        Assert.Equal(0, analysis.TextBasedPageCount);
        Assert.Collection(
            analysis.Pages,
            page => Assert.Equal(0.96, page.ImageCoverageRatio));
    }

    [Fact]
    public async Task AnalyzerVersionChangeInvalidatesReadyAnalysis()
    {
        await using var fixture = await ServiceFixture.CreateAsync();
        await fixture.Service.AnalyzeAsync(
            fixture.DocumentId,
            force: false,
            CancellationToken.None);

        fixture.Analyzer.Version = "test-analyzer-v2";
        var result = await fixture.Service.AnalyzeAsync(
            fixture.DocumentId,
            force: false,
            CancellationToken.None);

        Assert.False(result.Reused);
        Assert.Equal(2, fixture.Analyzer.CallCount);
        Assert.Equal("test-analyzer-v2", result.AnalyzerVersion);
    }

    [Fact]
    public async Task ExistingPdfWithoutAnalysisRowCanBeAnalyzed()
    {
        await using var fixture = await ServiceFixture.CreateAsync();
        Assert.Empty(await fixture.Context.DocumentTechnicalAnalyses.ToListAsync());

        await fixture.Service.AnalyzeAsync(
            fixture.DocumentId,
            force: false,
            CancellationToken.None);

        Assert.Single(await fixture.Context.DocumentTechnicalAnalyses.ToListAsync());
    }

    [Fact]
    public async Task DocxIsSkippedWithoutOpeningFileOrCallingAnalyzer()
    {
        await using var fixture = await ServiceFixture.CreateAsync(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "document.docx");

        var first = await fixture.Service.AnalyzeAsync(
            fixture.DocumentId,
            force: false,
            CancellationToken.None);
        var second = await fixture.Service.AnalyzeAsync(
            fixture.DocumentId,
            force: false,
            CancellationToken.None);

        Assert.Equal(DocumentTechnicalAnalysisStatus.Skipped, first.Status);
        Assert.True(second.Reused);
        Assert.Equal(0, fixture.Storage.OpenCount);
        Assert.Equal(0, fixture.Analyzer.CallCount);
        var persisted = await fixture.Context.DocumentTechnicalAnalyses.SingleAsync();
        Assert.Equal(DocumentTechnicalAnalysisStatus.Skipped, persisted.Status);
        Assert.Null(persisted.SourceFileHash);
    }

    [Fact]
    public async Task AnalyzerFailurePersistsOnlySafeFailedState()
    {
        await using var fixture = await ServiceFixture.CreateAsync();
        fixture.Analyzer.Exception = new InvalidOperationException(
            "Sensitive parser details should not be persisted.");

        var exception = await Assert.ThrowsAsync<PdfTechnicalAnalysisException>(() =>
            fixture.Service.AnalyzeAsync(
                fixture.DocumentId,
                force: false,
                CancellationToken.None));

        Assert.Equal(
            PdfTechnicalAnalysisArchitecture.SafeFailureMessage,
            exception.SafeMessage);
        fixture.Context.ChangeTracker.Clear();
        var persisted = await fixture.Context.DocumentTechnicalAnalyses.SingleAsync();
        Assert.Equal(DocumentTechnicalAnalysisStatus.Failed, persisted.Status);
        Assert.Equal(TechnicalType.Unknown, persisted.TechnicalType);
        Assert.Equal(
            PdfTechnicalAnalysisArchitecture.SafeFailureMessage,
            persisted.LastError);
        Assert.DoesNotContain("Sensitive", persisted.LastError, StringComparison.Ordinal);
        Assert.Empty(await fixture.Context.DocumentPageTechnicalAnalyses.ToListAsync());
    }

    [Fact]
    public async Task DeletingDocumentCascadesTechnicalAnalysisAndPages()
    {
        await using var fixture = await ServiceFixture.CreateAsync();
        await fixture.Service.AnalyzeAsync(
            fixture.DocumentId,
            force: false,
            CancellationToken.None);

        fixture.Context.ChangeTracker.Clear();
        var document = await fixture.Context.Documents
            .Include(item => item.TechnicalAnalysis!)
                .ThenInclude(analysis => analysis.Pages)
            .SingleAsync();
        fixture.Context.Documents.Remove(document);
        await fixture.Context.SaveChangesAsync();

        Assert.Empty(await fixture.Context.DocumentTechnicalAnalyses.ToListAsync());
        Assert.Empty(await fixture.Context.DocumentPageTechnicalAnalyses.ToListAsync());
    }

    private static PdfTechnicalAnalysisResult Result(
        TechnicalType type = TechnicalType.TextBased,
        double coverage = 0.05)
    {
        var metrics = type switch
        {
            TechnicalType.Scanned => new PdfPageTechnicalMetrics(1, 0, 0, 1, coverage),
            TechnicalType.ImageBased => new PdfPageTechnicalMetrics(1, 0, 0, 1, coverage),
            TechnicalType.Mixed => new PdfPageTechnicalMetrics(1, 80, 10, 1, coverage),
            TechnicalType.Unknown => new PdfPageTechnicalMetrics(1, 0, 0, 0, 0),
            _ => new PdfPageTechnicalMetrics(1, 80, 10, 1, coverage)
        };
        var page = PdfTechnicalClassifier.ClassifyPage(metrics);
        return new PdfTechnicalAnalysisResult(
            PdfTechnicalClassifier.ClassifyDocument([page]),
            [page]);
    }

    private sealed class ServiceFixture : IAsyncDisposable
    {
        private ServiceFixture(
            ApplicationDbContext context,
            Guid documentId,
            RecordingStorage storage,
            RecordingAnalyzer analyzer)
        {
            Context = context;
            DocumentId = documentId;
            Storage = storage;
            Analyzer = analyzer;
            Service = new DocumentTechnicalAnalysisService(
                context,
                storage,
                analyzer,
                new FixedTimeProvider(new DateTimeOffset(
                    2026,
                    9,
                    2,
                    10,
                    0,
                    0,
                    TimeSpan.Zero)),
                NullLogger<DocumentTechnicalAnalysisService>.Instance);
        }

        public ApplicationDbContext Context { get; }

        public Guid DocumentId { get; }

        public RecordingStorage Storage { get; }

        public RecordingAnalyzer Analyzer { get; }

        public DocumentTechnicalAnalysisService Service { get; }

        public static async Task<ServiceFixture> CreateAsync(
            string contentType = "application/pdf",
            string storedFileName = "document.pdf")
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"technical-analysis-{Guid.NewGuid():N}")
                .Options;
            var context = new ApplicationDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var now = DateTime.UtcNow;
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "technical@example.com",
                NormalizedUserName = "TECHNICAL@EXAMPLE.COM",
                Email = "technical@example.com",
                NormalizedEmail = "TECHNICAL@EXAMPLE.COM",
                DisplayName = "Technical Test",
                CreatedAtUtc = now
            };
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = "Technical tests",
                Owner = user,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            var document = new StoredDocument
            {
                Id = Guid.NewGuid(),
                Project = project,
                OriginalFileName = storedFileName,
                StoredFileName = storedFileName,
                ContentType = contentType,
                FileSizeBytes = 5,
                Status = DocumentStatus.Ready,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            context.Documents.Add(document);
            await context.SaveChangesAsync();

            return new ServiceFixture(
                context,
                document.Id,
                new RecordingStorage([1, 2, 3, 4, 5]),
                new RecordingAnalyzer { Result = Result() });
        }

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }

    private sealed class RecordingAnalyzer : IPdfTechnicalAnalyzer
    {
        public string Version { get; set; } = "test-analyzer-v1";

        public string AnalyzerVersion => Version;

        public int CallCount { get; private set; }

        public PdfTechnicalAnalysisResult Result { get; set; } = null!;

        public Exception? Exception { get; set; }

        public Task<PdfTechnicalAnalysisResult> AnalyzeAsync(
            Stream pdfStream,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Exception is null
                ? Task.FromResult(Result)
                : Task.FromException<PdfTechnicalAnalysisResult>(Exception);
        }
    }

    private sealed class RecordingStorage(byte[] bytes) : IFileStorageService
    {
        public byte[] Bytes { get; } = bytes;

        public int OpenCount { get; private set; }

        public Task<string> SaveAsync(
            Stream source,
            string fileExtension,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            string storedFileName,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ExistsAsync(
            string storedFileName,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<Stream> OpenReadAsync(
            string storedFileName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenCount++;
            return Task.FromResult<Stream>(new MemoryStream(Bytes, writable: false));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
