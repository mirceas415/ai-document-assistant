using System.Security.Claims;
using AI.DocumentAssistant.Server.Contracts;
using AI.DocumentAssistant.Server.Controllers;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Ocr;
using AI.DocumentAssistant.Server.Processing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StoredDocument = AI.DocumentAssistant.Server.Models.Document;

namespace AI.DocumentAssistant.Server.Tests;

public sealed class DocumentOcrControllerTests
{
    [Fact]
    public async Task UnauthenticatedRequestsAreRejectedBeforeRebuild()
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
        Assert.Equal(0, fixture.ProcessingService.RebuildCallCount);
    }

    [Fact]
    public async Task CrossUserAndCrossProjectRequestsReturnSafeNotFound()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        var otherUser = fixture.CreateController(fixture.OtherOwnerId);
        var owner = fixture.CreateController(fixture.OwnerId);

        var results = new IActionResult?[]
        {
            (await otherUser.Get(fixture.ProjectId, fixture.DocumentId, CancellationToken.None)).Result,
            (await otherUser.Rebuild(fixture.ProjectId, fixture.DocumentId, CancellationToken.None)).Result,
            (await owner.Get(fixture.OtherProjectId, fixture.DocumentId, CancellationToken.None)).Result,
            (await owner.Rebuild(fixture.OtherProjectId, fixture.DocumentId, CancellationToken.None)).Result
        };

        Assert.All(results, AssertSafeNotFound);
        Assert.Equal(0, fixture.ProcessingService.RebuildCallCount);
    }

    [Fact]
    public async Task LegacyPdfWithoutOcrAnalysisReturnsNotAnalyzed()
    {
        await using var fixture = await ControllerFixture.CreateAsync();

        var result = await fixture.CreateController(fixture.OwnerId).Get(
            fixture.ProjectId,
            fixture.DocumentId,
            CancellationToken.None);

        var response = Assert.IsType<DocumentOcrAnalysisResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(DocumentOcrStatus.NotAnalyzed, response.Status);
        Assert.Equal(OcrArchitecture.RoutingVersion, response.RoutingVersion);
        Assert.Empty(response.Pages);
    }

    [Fact]
    public async Task DocxIsNotApplicableAndCannotBeRebuilt()
    {
        await using var fixture = await ControllerFixture.CreateAsync(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "owned.docx");
        var controller = fixture.CreateController(fixture.OwnerId);

        var get = await controller.Get(
            fixture.ProjectId,
            fixture.DocumentId,
            CancellationToken.None);
        var rebuild = await controller.Rebuild(
            fixture.ProjectId,
            fixture.DocumentId,
            CancellationToken.None);

        var response = Assert.IsType<DocumentOcrAnalysisResponse>(
            Assert.IsType<OkObjectResult>(get.Result).Value);
        Assert.Equal(DocumentOcrStatus.Skipped, response.Status);
        var badRequest = Assert.IsType<BadRequestObjectResult>(rebuild.Result);
        Assert.Equal(
            "Local OCR is applicable only to PDF documents.",
            Assert.IsType<ApiErrorResponse>(badRequest.Value).Message);
        Assert.Equal(0, fixture.ProcessingService.RebuildCallCount);
    }

    [Fact]
    public async Task OwnerRebuildInvokesForcedPipelineAndReturnsPageDiagnostics()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        fixture.ProcessingService.OnRebuild = async (documentId, cancellationToken) =>
        {
            var analysis = new DocumentOcrAnalysis
            {
                DocumentId = documentId,
                Status = DocumentOcrStatus.Ready,
                CandidatePageCount = 1,
                SuccessfulPageCount = 1,
                FailedPageCount = 0,
                EngineName = "Tesseract",
                EngineVersion = "test-version",
                Languages = "ron+eng",
                RenderDpi = 300,
                MaxCandidatePages = 200,
                MaxRenderedPixels = 25_000_000,
                RoutingVersion = OcrArchitecture.RoutingVersion,
                RoutingHash = "routing-hash",
                ConfigurationHash = "configuration-hash",
                ProcessedAtUtc = DateTime.UtcNow
            };
            analysis.Pages.Add(new DocumentPageOcrResult
            {
                DocumentOcrAnalysisId = documentId,
                PageNumber = 2,
                Status = DocumentPageOcrStatus.Ready,
                SourceTechnicalType = TechnicalType.Scanned,
                RecognizedCharacterCount = 1_834,
                RecognizedWordCount = 280,
                MeanConfidence = 0.92,
                EffectiveRenderDpi = 300,
                RenderedWidthPixels = 2480,
                RenderedHeightPixels = 3508,
                UsedInExtraction = true
            });
            fixture.Context.DocumentOcrAnalyses.Add(analysis);
            await fixture.Context.SaveChangesAsync(cancellationToken);
        };

        var result = await fixture.CreateController(fixture.OwnerId).Rebuild(
            fixture.ProjectId,
            fixture.DocumentId,
            CancellationToken.None);

        var response = Assert.IsType<DocumentOcrAnalysisResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(1, fixture.ProcessingService.RebuildCallCount);
        Assert.Equal(fixture.DocumentId, fixture.ProcessingService.RebuiltDocumentId);
        Assert.Equal(DocumentOcrStatus.Ready, response.Status);
        Assert.Equal("Tesseract", response.EngineName);
        var page = Assert.Single(response.Pages);
        Assert.Equal(2, page.PageNumber);
        Assert.True(page.UsedInExtraction);
        Assert.Equal(TechnicalType.Scanned, page.SourceTechnicalType);
    }

    private static void AssertSafeNotFound(IActionResult? result)
    {
        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(
            "Project or document not found.",
            Assert.IsType<ApiErrorResponse>(notFound.Value).Message);
    }

    private sealed class ControllerFixture : IAsyncDisposable
    {
        private ControllerFixture(
            ApplicationDbContext context,
            Guid ownerId,
            Guid otherOwnerId,
            Guid projectId,
            Guid otherProjectId,
            Guid documentId)
        {
            Context = context;
            OwnerId = ownerId;
            OtherOwnerId = otherOwnerId;
            ProjectId = projectId;
            OtherProjectId = otherProjectId;
            DocumentId = documentId;
            ProcessingService = new RecordingProcessingService();
        }

        public ApplicationDbContext Context { get; }
        public Guid OwnerId { get; }
        public Guid OtherOwnerId { get; }
        public Guid ProjectId { get; }
        public Guid OtherProjectId { get; }
        public Guid DocumentId { get; }
        public RecordingProcessingService ProcessingService { get; }

        public static async Task<ControllerFixture> CreateAsync(
            string contentType = "application/pdf",
            string storedFileName = "owned.pdf")
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"ocr-controller-{Guid.NewGuid():N}")
                .Options;
            var context = new ApplicationDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var now = DateTime.UtcNow;
            var ownerId = Guid.NewGuid();
            var otherOwnerId = Guid.NewGuid();
            var owner = User(ownerId, "ocr-owner@example.com", now);
            var otherOwner = User(otherOwnerId, "other-ocr-owner@example.com", now);
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = "Owned project",
                Owner = owner,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            var otherProject = new Project
            {
                Id = Guid.NewGuid(),
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

            return new ControllerFixture(
                context,
                ownerId,
                otherOwnerId,
                project.Id,
                otherProject.Id,
                document.Id);
        }

        public DocumentOcrController CreateController(Guid? userId)
        {
            var claims = userId.HasValue
                ? new[] { new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()) }
                : [];
            return new DocumentOcrController(
                Context,
                ProcessingService,
                new OcrRoutingPolicy(),
                NullLogger<DocumentOcrController>.Instance)
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
            DisplayName = "OCR User",
            CreatedAtUtc = now
        };
    }

    private sealed class RecordingProcessingService : IDocumentProcessingService
    {
        public int RebuildCallCount { get; private set; }
        public Guid? RebuiltDocumentId { get; private set; }
        public Func<Guid, CancellationToken, Task>? OnRebuild { get; set; }

        public Task ProcessAsync(Guid documentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task RebuildOcrAsync(Guid documentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RebuildCallCount++;
            RebuiltDocumentId = documentId;
            if (OnRebuild is not null)
            {
                await OnRebuild(documentId, cancellationToken);
            }
        }
    }
}
