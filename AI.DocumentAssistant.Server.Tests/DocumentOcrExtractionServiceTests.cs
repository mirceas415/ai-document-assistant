using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Ocr;
using AI.DocumentAssistant.Server.Processing;
using AI.DocumentAssistant.Server.TechnicalAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Tests;

public sealed class DocumentOcrExtractionServiceTests
{
    [Theory]
    [InlineData(TechnicalType.Scanned, true)]
    [InlineData(TechnicalType.TextBased, false)]
    [InlineData(TechnicalType.Mixed, false)]
    [InlineData(TechnicalType.ImageBased, false)]
    [InlineData(TechnicalType.Unknown, false)]
    public void RoutingSelectsOnlyScannedPages(
        TechnicalType technicalType,
        bool expected)
    {
        var policy = new OcrRoutingPolicy();

        Assert.Equal(expected, policy.ShouldOcr(technicalType));
    }

    [Fact]
    public void RoutingHashIsDeterministicAndOrderIndependent()
    {
        var policy = new OcrRoutingPolicy();
        var first = Page(4, TechnicalType.Scanned);
        var second = Page(2, TechnicalType.Scanned);

        var left = policy.ComputeRoutingHash([first, second]);
        var right = policy.ComputeRoutingHash([second, first]);

        Assert.Equal(left, right);
        Assert.Equal(64, left.Length);
        Assert.Equal("ocr-routing-v1", policy.Version);
    }

    [Fact]
    public async Task ZeroCandidatesSkipsRendererAndOcrAndPreservesNativePages()
    {
        await using var fixture = await Fixture.CreateAsync(
            TechnicalType.TextBased,
            TechnicalType.Mixed,
            TechnicalType.ImageBased,
            TechnicalType.Unknown);
        var native = new[]
        {
            Native(1, "text"),
            Native(2, "mixed"),
            Native(3, "image fallback"),
            Native(4, "unknown fallback")
        };

        var result = await fixture.ApplyAsync(native);

        Assert.Equal([1, 2, 3, 4], result.Select(section => section.PageNumber));
        Assert.All(result, section =>
            Assert.Equal(DocumentTextExtractionMethod.NativePdf, section.ExtractionMethod));
        Assert.Empty(fixture.Renderer.Calls);
        Assert.Equal(0, fixture.Ocr.InfoCallCount);
        Assert.Equal(0, fixture.Ocr.PageCallCount);
        var analysis = await fixture.Context.DocumentOcrAnalyses.SingleAsync();
        Assert.Equal(DocumentOcrStatus.Skipped, analysis.Status);
        Assert.Equal(0, analysis.CandidatePageCount);
    }

    [Fact]
    public async Task MixedDocumentCombinesNativeAndOcrTextInPageOrder()
    {
        await using var fixture = await Fixture.CreateAsync(
            TechnicalType.TextBased,
            TechnicalType.Scanned,
            TechnicalType.Mixed,
            TechnicalType.Scanned);
        fixture.Ocr.Results[2] = OcrText("ocr two", 0.92);
        fixture.Ocr.Results[4] = OcrText("ocr four", 0.81);

        var result = await fixture.ApplyAsync([
            Native(1, "native one"),
            Native(3, "native three")
        ]);

        Assert.Equal([1, 2, 3, 4], result.Select(section => section.PageNumber));
        Assert.Equal(
            ["native one", "ocr two", "native three", "ocr four"],
            result.Select(section => section.Content));
        Assert.Equal(
            [
                DocumentTextExtractionMethod.NativePdf,
                DocumentTextExtractionMethod.Ocr,
                DocumentTextExtractionMethod.NativePdf,
                DocumentTextExtractionMethod.Ocr
            ],
            result.Select(section => section.ExtractionMethod));
        Assert.Equal([2, 4], fixture.Renderer.Calls.Select(call => call.PageNumber));
        Assert.Equal(2, fixture.Ocr.PageCallCount);

        var analysis = await fixture.Context.DocumentOcrAnalyses
            .Include(item => item.Pages)
            .SingleAsync();
        Assert.Equal(DocumentOcrStatus.Ready, analysis.Status);
        Assert.Equal(2, analysis.CandidatePageCount);
        Assert.Equal(2, analysis.SuccessfulPageCount);
        Assert.Equal(0, analysis.FailedPageCount);
        Assert.All(analysis.Pages, page => Assert.True(page.UsedInExtraction));
    }

    [Fact]
    public async Task EmptyOcrOutputCreatesNoPlaceholderAndIsRecordedAsFailure()
    {
        await using var fixture = await Fixture.CreateAsync(
            TechnicalType.TextBased,
            TechnicalType.Scanned);
        fixture.Ocr.Results[2] = OcrText(" \r\n ", 0.1);

        var result = await fixture.ApplyAsync([Native(1, "native remains")]);

        var section = Assert.Single(result);
        Assert.Equal("native remains", section.Content);
        Assert.DoesNotContain("OCR", section.Content, StringComparison.OrdinalIgnoreCase);
        var analysis = await fixture.Context.DocumentOcrAnalyses
            .Include(item => item.Pages)
            .SingleAsync();
        Assert.Equal(DocumentOcrStatus.Failed, analysis.Status);
        Assert.Equal(DocumentPageOcrStatus.Empty, Assert.Single(analysis.Pages).Status);
        Assert.Equal(0, analysis.SuccessfulPageCount);
        Assert.Equal(1, analysis.FailedPageCount);
    }

    [Fact]
    public async Task OneCandidateFailureProducesPartialAndKeepsUsablePages()
    {
        await using var fixture = await Fixture.CreateAsync(
            TechnicalType.TextBased,
            TechnicalType.Scanned,
            TechnicalType.Scanned);
        fixture.Ocr.Results[2] = OcrText("recognized scan");
        fixture.Ocr.Errors[3] = new OcrException("One OCR page failed safely.");

        var result = await fixture.ApplyAsync([Native(1, "native text")]);

        Assert.Equal([1, 2], result.Select(section => section.PageNumber));
        Assert.Equal(["native text", "recognized scan"], result.Select(section => section.Content));
        var analysis = await fixture.Context.DocumentOcrAnalyses.SingleAsync();
        Assert.Equal(DocumentOcrStatus.Partial, analysis.Status);
        Assert.Equal(1, analysis.SuccessfulPageCount);
        Assert.Equal(1, analysis.FailedPageCount);
    }

    [Fact]
    public async Task AllCandidatesFailCleanlyWithoutInventingContent()
    {
        await using var fixture = await Fixture.CreateAsync(
            TechnicalType.Scanned,
            TechnicalType.Scanned);
        fixture.Renderer.Errors[1] = new OcrException("Render failed safely.");
        fixture.Renderer.Errors[2] = new OcrException("Render failed safely.");

        var result = await fixture.ApplyAsync([]);

        Assert.Empty(result);
        Assert.Equal(0, fixture.Ocr.PageCallCount);
        var analysis = await fixture.Context.DocumentOcrAnalyses.SingleAsync();
        Assert.Equal(DocumentOcrStatus.Failed, analysis.Status);
        Assert.Equal(2, analysis.FailedPageCount);
        Assert.Equal(2, await fixture.Context.DocumentPageOcrResults.CountAsync());
    }

    [Fact]
    public async Task MissingOcrInfrastructureDoesNotRenderAndNativeTextRemainsUsable()
    {
        await using var fixture = await Fixture.CreateAsync(
            TechnicalType.TextBased,
            TechnicalType.Scanned,
            TechnicalType.Scanned,
            TechnicalType.Scanned,
            TechnicalType.Scanned,
            TechnicalType.Scanned);
        fixture.Options.MaxCandidatePages = 2;
        fixture.Ocr.InfoError = new OcrUnavailableException(
            OcrArchitecture.UnavailableMessage);

        var result = await fixture.ApplyAsync([Native(1, "native text")]);

        Assert.Equal("native text", Assert.Single(result).Content);
        Assert.Empty(fixture.Renderer.Calls);
        var analysis = await fixture.Context.DocumentOcrAnalyses.SingleAsync();
        Assert.Equal(DocumentOcrStatus.Failed, analysis.Status);
        Assert.Equal(5, analysis.CandidatePageCount);
        Assert.Equal(5, analysis.FailedPageCount);
        Assert.Equal(OcrArchitecture.UnavailableMessage, analysis.LastError);
        var pages = await fixture.Context.DocumentPageOcrResults
            .OrderBy(page => page.PageNumber)
            .ToArrayAsync();
        Assert.Equal(3, pages.Length);
        Assert.Equal(DocumentPageOcrStatus.SkippedLimit, pages[^1].Status);
    }

    [Fact]
    public async Task MatchingSuccessfulResultIsReusedButForceRerunsOcr()
    {
        await using var fixture = await Fixture.CreateAsync(
            TechnicalType.TextBased,
            TechnicalType.Scanned);
        fixture.Ocr.Results[2] = OcrText("stored ocr text");
        var first = await fixture.ApplyAsync([Native(1, "native")]);
        var ocrSection = first.Single(section => section.PageNumber == 2);
        fixture.Context.DocumentTextSections.Add(new DocumentTextSection
        {
            Id = Guid.NewGuid(),
            DocumentId = fixture.DocumentId,
            SectionIndex = 1,
            PageNumber = 2,
            Content = ocrSection.Content,
            ExtractionMethod = DocumentTextExtractionMethod.Ocr,
            CreatedAtUtc = DateTime.UtcNow
        });
        await fixture.Context.SaveChangesAsync();
        var callsAfterFirst = fixture.Renderer.Calls.Count;

        var reused = await fixture.ApplyAsync([Native(1, "native")]);
        var forced = await fixture.ApplyAsync([Native(1, "native")], force: true);

        Assert.Equal("stored ocr text", reused.Single(section => section.PageNumber == 2).Content);
        Assert.Equal("stored ocr text", forced.Single(section => section.PageNumber == 2).Content);
        Assert.Equal(callsAfterFirst + 1, fixture.Renderer.Calls.Count);
    }

    [Fact]
    public async Task SourceModelAndLanguageChangesInvalidateReuse()
    {
        await using var fixture = await Fixture.CreateAsync(
            TechnicalType.Scanned);
        fixture.Ocr.Results[1] = OcrText("ocr value");
        var first = await fixture.ApplyAsync([]);
        await fixture.StoreSectionsAsync(first);
        var initialCalls = fixture.Renderer.Calls.Count;

        fixture.Ocr.ModelFingerprint = new string('B', 64);
        await fixture.ApplyAsync([]);
        Assert.Equal(initialCalls + 1, fixture.Renderer.Calls.Count);

        await fixture.StoreSectionsAsync(first, replace: true);
        fixture.TechnicalAnalysis.SourceFileHash = new string('C', 64);
        await fixture.Context.SaveChangesAsync();
        await fixture.ApplyAsync([]);
        Assert.Equal(initialCalls + 2, fixture.Renderer.Calls.Count);

        await fixture.StoreSectionsAsync(first, replace: true);
        var languageService = fixture.CreateService(new OcrOptions
        {
            Languages = "eng",
            RenderDpi = 300,
            MaxCandidatePages = 200,
            MaxRenderedPixels = 25_000_000
        });
        await languageService.ApplyAsync(
            fixture.DocumentId,
            new MemoryStream([1]),
            [],
            force: false,
            CancellationToken.None);
        Assert.Equal(initialCalls + 3, fixture.Renderer.Calls.Count);
    }

    [Fact]
    public async Task CandidatePageLimitBoundsWorkAndProducesPartialStatus()
    {
        await using var fixture = await Fixture.CreateAsync(
            TechnicalType.Scanned,
            TechnicalType.Scanned,
            TechnicalType.Scanned,
            TechnicalType.Scanned,
            TechnicalType.Scanned);
        fixture.Options.MaxCandidatePages = 2;
        fixture.Ocr.Results[1] = OcrText("one");
        fixture.Ocr.Results[2] = OcrText("two");

        var result = await fixture.ApplyAsync([]);

        Assert.Equal([1, 2], fixture.Renderer.Calls.Select(call => call.PageNumber));
        Assert.Equal([1, 2], result.Select(section => section.PageNumber));
        var analysis = await fixture.Context.DocumentOcrAnalyses
            .Include(item => item.Pages)
            .SingleAsync();
        Assert.Equal(DocumentOcrStatus.Partial, analysis.Status);
        Assert.Equal(5, analysis.CandidatePageCount);
        Assert.Equal(2, analysis.SuccessfulPageCount);
        Assert.Equal(3, analysis.FailedPageCount);
        Assert.Equal(3, analysis.Pages.Count);
        Assert.Equal(
            DocumentPageOcrStatus.SkippedLimit,
            analysis.Pages.Single(page => page.PageNumber == 3).Status);
    }

    [Fact]
    public void RenderSafetyReducesDpiWithoutExceedingPixelBudget()
    {
        var plan = PdfRenderSafety.Calculate(
            widthPoints: 2_000,
            heightPoints: 3_000,
            requestedDpi: 300,
            maximumPixels: 25_000_000);

        Assert.True(plan.EffectiveDpi < 300);
        Assert.True((long)plan.WidthPixels * plan.HeightPixels <= 25_000_000);
        Assert.Equal(
            2d / 3d,
            (double)plan.WidthPixels / plan.HeightPixels,
            precision: 2);
    }

    [Fact]
    public async Task LegacyPdfWithoutTechnicalAnalysisKeepsNativeBehavior()
    {
        await using var fixture = await Fixture.CreateLegacyAsync();

        var result = await fixture.ApplyAsync([Native(1, "legacy native text")]);

        Assert.Equal("legacy native text", Assert.Single(result).Content);
        Assert.Empty(fixture.Renderer.Calls);
        var analysis = await fixture.Context.DocumentOcrAnalyses.SingleAsync();
        Assert.Equal(DocumentOcrStatus.NotAnalyzed, analysis.Status);
    }

    [Fact]
    public async Task DeletingDocumentCascadesOcrAnalysisAndPageResults()
    {
        await using var fixture = await Fixture.CreateAsync(TechnicalType.Scanned);
        fixture.Ocr.Results[1] = OcrText("recognized");
        await fixture.ApplyAsync([]);

        fixture.Context.Documents.Remove(
            await fixture.Context.Documents.SingleAsync());
        await fixture.Context.SaveChangesAsync();

        Assert.Empty(await fixture.Context.DocumentOcrAnalyses.ToListAsync());
        Assert.Empty(await fixture.Context.DocumentPageOcrResults.ToListAsync());
    }

    private static ExtractedTextSection Native(int pageNumber, string content) =>
        new(
            pageNumber - 1,
            content,
            pageNumber,
            ExtractionMethod: DocumentTextExtractionMethod.NativePdf);

    private static OcrPageResult OcrText(string text, double? confidence = 0.9) =>
        new(text, confidence, "Fake OCR", "1.0");

    private static DocumentPageTechnicalAnalysis Page(
        int pageNumber,
        TechnicalType type) =>
        new()
        {
            DocumentTechnicalAnalysisId = Guid.Empty,
            PageNumber = pageNumber,
            TechnicalType = type
        };

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            ApplicationDbContext context,
            Guid documentId,
            DocumentTechnicalAnalysis? technicalAnalysis)
        {
            Context = context;
            DocumentId = documentId;
            TechnicalAnalysis = technicalAnalysis!;
        }

        public ApplicationDbContext Context { get; }

        public Guid DocumentId { get; }

        public DocumentTechnicalAnalysis TechnicalAnalysis { get; }

        public FakeRenderer Renderer { get; } = new();

        public FakeOcrService Ocr { get; } = new();

        public OcrOptions Options { get; } = new();

        public static Task<Fixture> CreateAsync(params TechnicalType[] pageTypes) =>
            CreateCoreAsync(pageTypes, includeTechnicalAnalysis: true);

        public static Task<Fixture> CreateLegacyAsync() =>
            CreateCoreAsync([], includeTechnicalAnalysis: false);

        private static async Task<Fixture> CreateCoreAsync(
            IReadOnlyList<TechnicalType> pageTypes,
            bool includeTechnicalAnalysis)
        {
            var databaseOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"ocr-extraction-{Guid.NewGuid():N}")
                .Options;
            var context = new ApplicationDbContext(databaseOptions);
            await context.Database.EnsureCreatedAsync();
            var now = DateTime.UtcNow;
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "ocr@example.com",
                NormalizedUserName = "OCR@EXAMPLE.COM",
                Email = "ocr@example.com",
                NormalizedEmail = "OCR@EXAMPLE.COM",
                DisplayName = "OCR Test",
                CreatedAtUtc = now
            };
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = "OCR tests",
                Owner = user,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            var document = new Document
            {
                Id = Guid.NewGuid(),
                Project = project,
                OriginalFileName = "test.pdf",
                StoredFileName = "test.pdf",
                ContentType = OcrArchitecture.PdfContentType,
                FileSizeBytes = 10,
                Status = DocumentStatus.Ready,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            DocumentTechnicalAnalysis? technical = null;
            if (includeTechnicalAnalysis)
            {
                technical = new DocumentTechnicalAnalysis
                {
                    DocumentId = document.Id,
                    Document = document,
                    Status = DocumentTechnicalAnalysisStatus.Ready,
                    TechnicalType = PdfTechnicalClassifier.ClassifyDocument(
                        pageTypes.Select((type, index) =>
                            new PdfPageTechnicalAnalysisResult(
                                index + 1,
                                type,
                                0,
                                0,
                                0,
                                0,
                                false,
                                type == TechnicalType.Scanned)).ToArray()),
                    PageCount = pageTypes.Count,
                    SourceFileHash = new string('A', 64),
                    AnalyzerVersion = "test-v1",
                    AnalyzedAtUtc = now
                };
                foreach (var (type, index) in pageTypes.Select((type, index) => (type, index)))
                {
                    technical.Pages.Add(new DocumentPageTechnicalAnalysis
                    {
                        DocumentTechnicalAnalysisId = document.Id,
                        PageNumber = index + 1,
                        TechnicalType = type,
                        HasPageSizedImage = type == TechnicalType.Scanned
                    });
                }

                document.TechnicalAnalysis = technical;
            }

            context.Documents.Add(document);
            await context.SaveChangesAsync();
            return new Fixture(context, document.Id, technical);
        }

        public Task<IReadOnlyList<ExtractedTextSection>> ApplyAsync(
            IReadOnlyList<ExtractedTextSection> native,
            bool force = false) =>
            CreateService(Options).ApplyAsync(
                DocumentId,
                new MemoryStream([1, 2, 3]),
                native,
                force,
                CancellationToken.None);

        public DocumentOcrExtractionService CreateService(OcrOptions options) =>
            new(
                Context,
                Renderer,
                Ocr,
                new OcrRoutingPolicy(),
                Microsoft.Extensions.Options.Options.Create(options),
                TimeProvider.System,
                NullLogger<DocumentOcrExtractionService>.Instance);

        public async Task StoreSectionsAsync(
            IReadOnlyList<ExtractedTextSection> sections,
            bool replace = false)
        {
            if (replace)
            {
                Context.DocumentTextSections.RemoveRange(
                    await Context.DocumentTextSections.ToListAsync());
            }

            Context.DocumentTextSections.AddRange(sections.Select(section =>
                new DocumentTextSection
                {
                    Id = Guid.NewGuid(),
                    DocumentId = DocumentId,
                    SectionIndex = section.SectionIndex,
                    Content = section.Content,
                    PageNumber = section.PageNumber,
                    ExtractionMethod = section.ExtractionMethod,
                    CreatedAtUtc = DateTime.UtcNow
                }));
            await Context.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }

    private sealed class FakeRenderer : IPdfPageRenderer
    {
        public List<RenderCall> Calls { get; } = [];

        public Dictionary<int, Exception> Errors { get; } = [];

        public Task<OcrImage> RenderPageAsync(
            Stream pdfStream,
            int pageNumber,
            int requestedDpi,
            long maximumPixels,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(new RenderCall(pageNumber, requestedDpi, maximumPixels));
            if (Errors.TryGetValue(pageNumber, out var error))
            {
                return Task.FromException<OcrImage>(error);
            }

            return Task.FromResult(new OcrImage(
                new MemoryStream([(byte)pageNumber]),
                100,
                200,
                requestedDpi));
        }
    }

    private sealed record RenderCall(
        int PageNumber,
        int RequestedDpi,
        long MaximumPixels);

    private sealed class FakeOcrService : IOcrService
    {
        public string EngineName => "Fake OCR";

        public string EngineVersion => "1.0";

        public string ModelFingerprint { get; set; } = new string('A', 64);

        public int InfoCallCount { get; private set; }

        public int PageCallCount { get; private set; }

        public Exception? InfoError { get; set; }

        public Dictionary<int, OcrPageResult> Results { get; } = [];

        public Dictionary<int, Exception> Errors { get; } = [];

        public Task<OcrEngineInfo> GetEngineInfoAsync(
            string languages,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InfoCallCount++;
            return InfoError is null
                ? Task.FromResult(new OcrEngineInfo(
                    EngineName,
                    EngineVersion,
                    ModelFingerprint))
                : Task.FromException<OcrEngineInfo>(InfoError);
        }

        public Task<OcrPageResult> OcrPageAsync(
            OcrImage image,
            string languages,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PageCallCount++;
            var pageNumber = image.OpenContent().ReadByte();
            if (Errors.TryGetValue(pageNumber, out var error))
            {
                return Task.FromException<OcrPageResult>(error);
            }

            return Task.FromResult(Results.GetValueOrDefault(
                pageNumber,
                OcrText($"ocr page {pageNumber}")));
        }
    }
}
