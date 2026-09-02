using System.Security.Claims;
using AI.DocumentAssistant.Server.Contracts;
using AI.DocumentAssistant.Server.Controllers;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Storage;
using AI.DocumentAssistant.Server.TechnicalAnalysis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StoredDocument = AI.DocumentAssistant.Server.Models.Document;

namespace AI.DocumentAssistant.Server.Tests;

public sealed class DocumentTechnicalAnalysisControllerTests
{
    [Fact]
    public async Task UnauthenticatedGetAndRebuildAreRejectedBeforeAnalysis()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        var controller = fixture.CreateController(null);

        var get = await controller.Get(
            fixture.ProjectId,
            fixture.DocumentId,
            CancellationToken.None);
        var rebuild = await controller.Rebuild(
            fixture.ProjectId,
            fixture.DocumentId,
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(get.Result);
        Assert.IsType<UnauthorizedObjectResult>(rebuild.Result);
        Assert.Equal(0, fixture.Analyzer.CallCount);
    }

    [Fact]
    public async Task CrossUserAndCrossProjectRequestsReturnSafeNotFound()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        var otherUser = fixture.CreateController(fixture.OtherOwnerId);
        var owner = fixture.CreateController(fixture.OwnerId);

        var crossUserGet = await otherUser.Get(
            fixture.ProjectId,
            fixture.DocumentId,
            CancellationToken.None);
        var crossUserRebuild = await otherUser.Rebuild(
            fixture.ProjectId,
            fixture.DocumentId,
            CancellationToken.None);
        var crossProjectGet = await owner.Get(
            fixture.OtherProjectId,
            fixture.DocumentId,
            CancellationToken.None);
        var crossProjectRebuild = await owner.Rebuild(
            fixture.OtherProjectId,
            fixture.DocumentId,
            CancellationToken.None);

        AssertSafeNotFound(crossUserGet.Result);
        AssertSafeNotFound(crossUserRebuild.Result);
        AssertSafeNotFound(crossProjectGet.Result);
        AssertSafeNotFound(crossProjectRebuild.Result);
        Assert.Equal(0, fixture.Analyzer.CallCount);
    }

    [Fact]
    public async Task ExistingPdfWithoutAnalysisReturnsNotAnalyzed()
    {
        await using var fixture = await ControllerFixture.CreateAsync();

        var result = await fixture.CreateController(fixture.OwnerId).Get(
            fixture.ProjectId,
            fixture.DocumentId,
            CancellationToken.None);

        var response = Assert.IsType<DocumentTechnicalAnalysisResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(DocumentTechnicalAnalysisStatus.NotAnalyzed, response.Status);
        Assert.Equal(TechnicalType.Unknown, response.TechnicalType);
        Assert.Empty(response.Pages);
        Assert.Null(response.SourceFileHash);
    }

    [Fact]
    public async Task OwnerRebuildForcesAnalysisAndReturnsOrderedPageDiagnostics()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        await fixture.Service.AnalyzeAsync(
            fixture.DocumentId,
            force: false,
            CancellationToken.None);
        fixture.Analyzer.Result = Result(
            PdfTechnicalClassifier.ClassifyPage(
                new PdfPageTechnicalMetrics(1, 0, 0, 1, 0.96)),
            PdfTechnicalClassifier.ClassifyPage(
                new PdfPageTechnicalMetrics(2, 80, 10, 0, 0)));

        var result = await fixture.CreateController(fixture.OwnerId).Rebuild(
            fixture.ProjectId,
            fixture.DocumentId,
            CancellationToken.None);

        var response = Assert.IsType<DocumentTechnicalAnalysisResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(2, fixture.Analyzer.CallCount);
        Assert.Equal(DocumentTechnicalAnalysisStatus.Ready, response.Status);
        Assert.Equal(TechnicalType.Mixed, response.TechnicalType);
        Assert.Equal(2, response.PageCount);
        Assert.Equal(1, response.TextBasedPageCount);
        Assert.Equal(1, response.ScannedPageCount);
        Assert.Equal([1, 2], response.Pages.Select(page => page.PageNumber));
        Assert.Equal("controller-analyzer-v1", response.AnalyzerVersion);
        Assert.NotNull(response.SourceFileHash);
        Assert.NotNull(response.AnalyzedAtUtc);
    }

    [Fact]
    public async Task DocxGetIsNotApplicableAndRebuildIsRejectedWithoutAnalysis()
    {
        await using var fixture = await ControllerFixture.CreateAsync(
            contentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            storedFileName: "owned.docx");
        var controller = fixture.CreateController(fixture.OwnerId);

        var get = await controller.Get(
            fixture.ProjectId,
            fixture.DocumentId,
            CancellationToken.None);
        var rebuild = await controller.Rebuild(
            fixture.ProjectId,
            fixture.DocumentId,
            CancellationToken.None);

        var response = Assert.IsType<DocumentTechnicalAnalysisResponse>(
            Assert.IsType<OkObjectResult>(get.Result).Value);
        Assert.Equal(DocumentTechnicalAnalysisStatus.Skipped, response.Status);
        var badRequest = Assert.IsType<BadRequestObjectResult>(rebuild.Result);
        Assert.Equal(
            PdfTechnicalAnalysisArchitecture.NotApplicableMessage,
            Assert.IsType<ApiErrorResponse>(badRequest.Value).Message);
        Assert.Equal(0, fixture.Analyzer.CallCount);
        Assert.Equal(0, fixture.Storage.OpenCount);
    }

    [Fact]
    public async Task ActiveTechnicalAnalysisRejectsRebuild()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        fixture.Context.DocumentTechnicalAnalyses.Add(new DocumentTechnicalAnalysis
        {
            DocumentId = fixture.DocumentId,
            Status = DocumentTechnicalAnalysisStatus.Processing,
            TechnicalType = TechnicalType.Unknown,
            SourceFileHash = new string('A', 64),
            AnalyzerVersion = "controller-analyzer-v1"
        });
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.CreateController(fixture.OwnerId).Rebuild(
            fixture.ProjectId,
            fixture.DocumentId,
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(0, fixture.Analyzer.CallCount);
    }

    private static void AssertSafeNotFound(IActionResult? result)
    {
        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(
            "Project or document not found.",
            Assert.IsType<ApiErrorResponse>(notFound.Value).Message);
    }

    private static PdfTechnicalAnalysisResult Result(
        params PdfPageTechnicalAnalysisResult[] pages) =>
        new(PdfTechnicalClassifier.ClassifyDocument(pages), pages);

    private sealed class ControllerFixture : IAsyncDisposable
    {
        private ControllerFixture(
            ApplicationDbContext context,
            Guid ownerId,
            Guid otherOwnerId,
            Guid projectId,
            Guid otherProjectId,
            Guid documentId,
            RecordingStorage storage,
            RecordingAnalyzer analyzer)
        {
            Context = context;
            OwnerId = ownerId;
            OtherOwnerId = otherOwnerId;
            ProjectId = projectId;
            OtherProjectId = otherProjectId;
            DocumentId = documentId;
            Storage = storage;
            Analyzer = analyzer;
            Service = new DocumentTechnicalAnalysisService(
                context,
                storage,
                analyzer,
                TimeProvider.System,
                NullLogger<DocumentTechnicalAnalysisService>.Instance);
        }

        public ApplicationDbContext Context { get; }

        public Guid OwnerId { get; }

        public Guid OtherOwnerId { get; }

        public Guid ProjectId { get; }

        public Guid OtherProjectId { get; }

        public Guid DocumentId { get; }

        public RecordingStorage Storage { get; }

        public RecordingAnalyzer Analyzer { get; }

        public DocumentTechnicalAnalysisService Service { get; }

        public static async Task<ControllerFixture> CreateAsync(
            string contentType = "application/pdf",
            string storedFileName = "owned.pdf")
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"technical-controller-{Guid.NewGuid():N}")
                .Options;
            var context = new ApplicationDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var now = DateTime.UtcNow;
            var ownerId = Guid.NewGuid();
            var otherOwnerId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var otherProjectId = Guid.NewGuid();
            var owner = User(ownerId, "technical-owner@example.com", now);
            var otherOwner = User(otherOwnerId, "other-owner@example.com", now);
            var project = new Project
            {
                Id = projectId,
                Name = "Owned project",
                Owner = owner,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            var otherProject = new Project
            {
                Id = otherProjectId,
                Name = "Other route project",
                Owner = owner,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            context.Projects.AddRange(
                project,
                otherProject,
                new Project
                {
                    Id = Guid.NewGuid(),
                    Name = "Other owner project",
                    Owner = otherOwner,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            var document = new StoredDocument
            {
                Id = Guid.NewGuid(),
                Project = project,
                OriginalFileName = storedFileName,
                StoredFileName = storedFileName,
                ContentType = contentType,
                FileSizeBytes = 64,
                Status = DocumentStatus.Ready,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                ProcessedAtUtc = now
            };
            context.Documents.Add(document);
            await context.SaveChangesAsync();

            var defaultPage = PdfTechnicalClassifier.ClassifyPage(
                new PdfPageTechnicalMetrics(1, 80, 10, 0, 0));
            return new ControllerFixture(
                context,
                ownerId,
                otherOwnerId,
                projectId,
                otherProjectId,
                document.Id,
                new RecordingStorage(),
                new RecordingAnalyzer { Result = Result(defaultPage) });
        }

        public DocumentTechnicalAnalysisController CreateController(Guid? userId)
        {
            var claims = userId.HasValue
                ? new[] { new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()) }
                : [];
            return new DocumentTechnicalAnalysisController(
                Context,
                Service,
                NullLogger<DocumentTechnicalAnalysisController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
                    }
                }
            };
        }

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();

        private static ApplicationUser User(Guid id, string email, DateTime now) => new()
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = "Technical User",
            CreatedAtUtc = now
        };
    }

    private sealed class RecordingAnalyzer : IPdfTechnicalAnalyzer
    {
        public string AnalyzerVersion => "controller-analyzer-v1";

        public int CallCount { get; private set; }

        public PdfTechnicalAnalysisResult Result { get; set; } = null!;

        public Task<PdfTechnicalAnalysisResult> AnalyzeAsync(
            Stream pdfStream,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingStorage : IFileStorageService
    {
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
            return Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
        }
    }
}
