using System.Security.Claims;
using AI.DocumentAssistant.Server.Chunking;
using AI.DocumentAssistant.Server.Contracts;
using AI.DocumentAssistant.Server.Controllers;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Rag;
using AI.DocumentAssistant.Server.Understanding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StoredDocument = AI.DocumentAssistant.Server.Models.Document;

namespace AI.DocumentAssistant.Server.Tests;

public sealed class DocumentUnderstandingControllerTests
{
    [Fact]
    public async Task UnauthenticatedGetAndRebuildAreRejectedBeforeProviderCall()
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
        Assert.Empty(fixture.Client.Calls);
    }

    [Fact]
    public async Task CrossUserAndRouteProjectMismatchReturnSafeNotFoundWithoutProviderCall()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        var otherUserController = fixture.CreateController(fixture.OtherOwnerId);
        var ownerController = fixture.CreateController(fixture.OwnerId);

        var crossUserGet = await otherUserController.Get(
            fixture.ProjectId,
            fixture.DocumentId,
            CancellationToken.None);
        var crossUserRebuild = await otherUserController.Rebuild(
            fixture.ProjectId,
            fixture.DocumentId,
            CancellationToken.None);
        var crossProjectGet = await ownerController.Get(
            fixture.OtherProjectId,
            fixture.DocumentId,
            CancellationToken.None);
        var crossProjectRebuild = await ownerController.Rebuild(
            fixture.OtherProjectId,
            fixture.DocumentId,
            CancellationToken.None);

        AssertSafeNotFound(crossUserGet.Result);
        AssertSafeNotFound(crossUserRebuild.Result);
        AssertSafeNotFound(crossProjectGet.Result);
        AssertSafeNotFound(crossProjectRebuild.Result);
        Assert.Empty(fixture.Client.Calls);
        Assert.Empty(await fixture.Context.DocumentUnderstandings.ToArrayAsync());
    }

    [Fact]
    public async Task LegacyDocumentWithoutUnderstandingReturnsNotAnalyzed()
    {
        await using var fixture = await ControllerFixture.CreateAsync();

        var result = await fixture.CreateController(fixture.OwnerId).Get(
            fixture.ProjectId,
            fixture.DocumentId,
            CancellationToken.None);

        var response = Assert.IsType<DocumentUnderstandingResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(DocumentUnderstandingStatus.NotAnalyzed, response.Status);
        Assert.Null(response.DocumentType);
        Assert.Empty(response.Metadata);
        Assert.Null(response.Model);
        Assert.Null(response.PromptVersion);
        Assert.Null(response.SourceContentHash);
        Assert.Null(response.LastError);
        Assert.Empty(fixture.Client.Calls);
    }

    [Fact]
    public async Task OwnerRebuildForcesCurrentResultAndReturnsOrderedSafeResponse()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        fixture.Client.Result = Result(
            "Report",
            "Initial Report",
            "en",
            [new("Topic", "topic", "Initial", 0.8)]);
        await fixture.Service.AnalyzePersistedAsync(
            fixture.DocumentId,
            force: false,
            CancellationToken.None);
        fixture.Client.Result = Result(
            "Invoice",
            "Commercial Invoice",
            "EN-us",
            [
                new("Identifier", "invoice number", "INV-2026-14", 0.97),
                new("Date", "issue date", "14 March 2026", 0.91)
            ]);

        var result = await fixture.CreateController(fixture.OwnerId).Rebuild(
            fixture.ProjectId,
            fixture.DocumentId,
            CancellationToken.None);

        var response = Assert.IsType<DocumentUnderstandingResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(2, fixture.Client.Calls.Count);
        Assert.Equal(DocumentUnderstandingStatus.Ready, response.Status);
        Assert.Equal(DocumentType.Invoice, response.DocumentType);
        Assert.Equal("Commercial Invoice", response.DocumentSubtype);
        Assert.Equal("en-US", response.PrimaryLanguageCode);
        Assert.Equal("controller-understanding-model", response.Model);
        Assert.Equal(DocumentUnderstandingArchitecture.PromptVersion, response.PromptVersion);
        Assert.NotNull(response.SourceContentHash);
        Assert.NotNull(response.AnalyzedAtUtc);
        Assert.Null(response.LastError);
        Assert.Collection(
            response.Metadata,
            identifier =>
            {
                Assert.Equal(0, identifier.Sequence);
                Assert.Equal(DocumentMetadataKind.Identifier, identifier.Kind);
                Assert.Equal("invoice_number", identifier.Label);
                Assert.Equal("INV-2026-14", identifier.NormalizedValue);
            },
            date =>
            {
                Assert.Equal(1, date.Sequence);
                Assert.Equal(DocumentMetadataKind.Date, date.Kind);
                Assert.Equal("issue_date", date.Label);
                Assert.Equal("2026-03-14", date.NormalizedValue);
            });
    }

    [Fact]
    public async Task ProviderFailureReturnsSafeErrorAndLeavesFailedStateAvailableToGet()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        fixture.Client.Exception = new InvalidOperationException(
            "provider-body-and-secret-test-value");
        var controller = fixture.CreateController(fixture.OwnerId);

        var rebuild = await controller.Rebuild(
            fixture.ProjectId,
            fixture.DocumentId,
            CancellationToken.None);
        var get = await controller.Get(
            fixture.ProjectId,
            fixture.DocumentId,
            CancellationToken.None);

        var errorResult = Assert.IsType<ObjectResult>(rebuild.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, errorResult.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(errorResult.Value);
        Assert.Equal(DocumentUnderstandingArchitecture.SafeFailureMessage, error.Message);
        Assert.DoesNotContain("provider-body", error.Message, StringComparison.Ordinal);
        var response = Assert.IsType<DocumentUnderstandingResponse>(
            Assert.IsType<OkObjectResult>(get.Result).Value);
        Assert.Equal(DocumentUnderstandingStatus.Failed, response.Status);
        Assert.Equal(DocumentUnderstandingArchitecture.SafeFailureMessage, response.LastError);
        Assert.DoesNotContain("provider-body", response.LastError, StringComparison.Ordinal);
        Assert.Single(fixture.Client.Calls);
    }

    [Fact]
    public async Task RebuildRejectsNonReadyMissingNormalizedTextAndActiveUnderstanding()
    {
        await using var nonReady = await ControllerFixture.CreateAsync(
            documentStatus: DocumentStatus.Processing);
        var nonReadyResult = await nonReady.CreateController(nonReady.OwnerId).Rebuild(
            nonReady.ProjectId,
            nonReady.DocumentId,
            CancellationToken.None);

        await using var missingText = await ControllerFixture.CreateAsync(
            includeNormalizedText: false);
        var missingTextResult = await missingText.CreateController(missingText.OwnerId).Rebuild(
            missingText.ProjectId,
            missingText.DocumentId,
            CancellationToken.None);

        await using var active = await ControllerFixture.CreateAsync();
        active.Context.DocumentUnderstandings.Add(new DocumentUnderstanding
        {
            DocumentId = active.DocumentId,
            Status = DocumentUnderstandingStatus.Processing,
            Model = "controller-understanding-model",
            PromptVersion = DocumentUnderstandingArchitecture.PromptVersion,
            SourceContentHash = new string('A', DocumentUnderstandingLimits.SourceContentHashLength)
        });
        await active.Context.SaveChangesAsync();
        var activeResult = await active.CreateController(active.OwnerId).Rebuild(
            active.ProjectId,
            active.DocumentId,
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(nonReadyResult.Result);
        Assert.IsType<ConflictObjectResult>(missingTextResult.Result);
        Assert.IsType<ConflictObjectResult>(activeResult.Result);
        Assert.Empty(nonReady.Client.Calls);
        Assert.Empty(missingText.Client.Calls);
        Assert.Empty(active.Client.Calls);
    }

    [Fact]
    public async Task GetOrdersPersistedMetadataByAuthoritativeSequence()
    {
        await using var fixture = await ControllerFixture.CreateAsync();
        var understanding = new DocumentUnderstanding
        {
            DocumentId = fixture.DocumentId,
            Status = DocumentUnderstandingStatus.Ready,
            DocumentType = DocumentType.Form,
            DocumentTypeConfidence = 0.8,
            PrimaryLanguageCode = "ro",
            LanguageConfidence = 0.9,
            Model = "audit-model",
            PromptVersion = DocumentUnderstandingArchitecture.PromptVersion,
            SourceContentHash = new string('B', DocumentUnderstandingLimits.SourceContentHashLength),
            AnalyzedAtUtc = DateTime.UtcNow
        };
        understanding.MetadataEntries.Add(new DocumentMetadataEntry
        {
            Id = Guid.NewGuid(),
            Sequence = 1,
            Kind = DocumentMetadataKind.Person,
            Label = "recipient",
            Value = "Second Person",
            NormalizedValue = "Second Person",
            Confidence = 0.7
        });
        understanding.MetadataEntries.Add(new DocumentMetadataEntry
        {
            Id = Guid.NewGuid(),
            Sequence = 0,
            Kind = DocumentMetadataKind.Organization,
            Label = "issuer",
            Value = "First Organization",
            NormalizedValue = "First Organization",
            Confidence = 0.8
        });
        fixture.Context.DocumentUnderstandings.Add(understanding);
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.CreateController(fixture.OwnerId).Get(
            fixture.ProjectId,
            fixture.DocumentId,
            CancellationToken.None);

        var response = Assert.IsType<DocumentUnderstandingResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal([0, 1], response.Metadata.Select(value => value.Sequence));
        Assert.Equal(
            ["First Organization", "Second Person"],
            response.Metadata.Select(value => value.Value));
        Assert.Empty(fixture.Client.Calls);
    }

    private static void AssertSafeNotFound(IActionResult? result)
    {
        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var error = Assert.IsType<ApiErrorResponse>(notFound.Value);
        Assert.Equal("Project or document not found.", error.Message);
    }

    private static DocumentUnderstandingProviderResult Result(
        string documentType = "Report",
        string? subtype = "General Report",
        string language = "en",
        IReadOnlyList<DocumentUnderstandingProviderMetadataEntry>? metadata = null) =>
        new(
            documentType,
            subtype,
            0.9,
            language,
            0.95,
            "Detected title",
            "Detected subject",
            metadata ?? []);

    private sealed class ControllerFixture : IAsyncDisposable
    {
        private ControllerFixture(
            ApplicationDbContext context,
            Guid ownerId,
            Guid otherOwnerId,
            Guid projectId,
            Guid otherProjectId,
            Guid documentId,
            RecordingClient client,
            DocumentUnderstandingService service)
        {
            Context = context;
            OwnerId = ownerId;
            OtherOwnerId = otherOwnerId;
            ProjectId = projectId;
            OtherProjectId = otherProjectId;
            DocumentId = documentId;
            Client = client;
            Service = service;
        }

        public ApplicationDbContext Context { get; }

        public Guid OwnerId { get; }

        public Guid OtherOwnerId { get; }

        public Guid ProjectId { get; }

        public Guid OtherProjectId { get; }

        public Guid DocumentId { get; }

        public RecordingClient Client { get; }

        public DocumentUnderstandingService Service { get; }

        public static async Task<ControllerFixture> CreateAsync(
            DocumentStatus documentStatus = DocumentStatus.Ready,
            bool includeNormalizedText = true)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"understanding-controller-{Guid.NewGuid():N}")
                .Options;
            var context = new ApplicationDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var now = DateTime.UtcNow;
            var ownerId = Guid.NewGuid();
            var otherOwnerId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var otherProjectId = Guid.NewGuid();
            var owner = CreateUser(ownerId, "understanding-owner@example.com", now);
            var otherOwner = CreateUser(otherOwnerId, "other-owner@example.com", now);
            var project = new Project
            {
                Id = projectId,
                Name = "Owned workspace",
                Owner = owner,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            var otherProject = new Project
            {
                Id = otherProjectId,
                Name = "Other route workspace",
                Owner = owner,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            var otherOwnerProject = new Project
            {
                Id = Guid.NewGuid(),
                Name = "Other user's workspace",
                Owner = otherOwner,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            var document = new StoredDocument
            {
                Id = Guid.NewGuid(),
                Project = project,
                OriginalFileName = "owned-document.pdf",
                StoredFileName = $"{Guid.NewGuid():N}.pdf",
                ContentType = "application/pdf",
                FileSizeBytes = 128,
                Status = documentStatus,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                ProcessedAtUtc = documentStatus == DocumentStatus.Ready ? now : null,
                NormalizedAtUtc = includeNormalizedText ? now : null
            };
            context.Projects.AddRange(project, otherProject, otherOwnerProject);
            context.Documents.Add(document);
            if (includeNormalizedText)
            {
                context.DocumentTextSections.Add(new DocumentTextSection
                {
                    Id = Guid.NewGuid(),
                    DocumentId = document.Id,
                    SectionIndex = 0,
                    Content = UsefulPersistedText(),
                    NormalizedContent = UsefulPersistedText(),
                    PageNumber = 1,
                    SectionTitle = "Generic document",
                    NormalizedAtUtc = now,
                    CreatedAtUtc = now
                });
            }

            await context.SaveChangesAsync();

            var client = new RecordingClient { Result = Result() };
            var service = new DocumentUnderstandingService(
                context,
                new DocumentUnderstandingInputBuilder(new Cl100kDocumentTokenizer()),
                client,
                new DocumentUnderstandingValidator(),
                Options.Create(new OpenAIDocumentUnderstandingOptions
                {
                    DocumentUnderstandingModel = "controller-understanding-model"
                }),
                Options.Create(new OpenAIAnswerOptions
                {
                    AnswerModel = "controller-answer-model"
                }),
                TimeProvider.System,
                NullLogger<DocumentUnderstandingService>.Instance);

            return new ControllerFixture(
                context,
                ownerId,
                otherOwnerId,
                projectId,
                otherProjectId,
                document.Id,
                client,
                service);
        }

        public DocumentUnderstandingController CreateController(Guid? userId)
        {
            var claims = userId.HasValue
                ? new[] { new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()) }
                : [];
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            };

            return new DocumentUnderstandingController(
                Context,
                Service,
                NullLogger<DocumentUnderstandingController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext }
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
        }

        private static ApplicationUser CreateUser(
            Guid id,
            string email,
            DateTime now) =>
            new()
            {
                Id = id,
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                DisplayName = "Understanding User",
                CreatedAtUtc = now
            };

        private static string UsefulPersistedText() => string.Join(
            ' ',
            Enumerable.Repeat(
                "This normalized document contains explicit generic business facts for classification and metadata extraction.",
                8));
    }

    private sealed class RecordingClient : IDocumentUnderstandingClient
    {
        public List<ClientCall> Calls { get; } = [];

        public DocumentUnderstandingProviderResult Result { get; set; } =
            DocumentUnderstandingControllerTests.Result();

        public Exception? Exception { get; set; }

        public Task<DocumentUnderstandingProviderResult> AnalyzeAsync(
            string model,
            string documentContent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(new ClientCall(model, documentContent));
            return Exception is null
                ? Task.FromResult(Result)
                : Task.FromException<DocumentUnderstandingProviderResult>(Exception);
        }
    }

    private sealed record ClientCall(string Model, string Content);
}
