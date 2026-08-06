using System.Security.Claims;
using AI.DocumentAssistant.Server.Chunking;
using AI.DocumentAssistant.Server.Contracts;
using AI.DocumentAssistant.Server.Controllers;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Normalization;
using AI.DocumentAssistant.Server.Processing;
using AI.DocumentAssistant.Server.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StoredDocument = AI.DocumentAssistant.Server.Models.Document;

namespace AI.DocumentAssistant.Server.Tests;

public sealed class DocumentsControllerAuthorizationTests
{
    [Fact]
    public async Task UnauthenticatedProcessingRequestIsRejected()
    {
        await using var database = await ControllerTestDatabase.CreateAsync();
        var controller = database.CreateController(null);

        var result = await controller.Process(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Empty(database.Queue.EnqueuedDocumentIds);
    }

    [Fact]
    public async Task UnauthenticatedChunkRebuildIsRejected()
    {
        await using var database = await ControllerTestDatabase.CreateAsync(
            DocumentStatus.Ready,
            includeText: true);
        var controller = database.CreateController(null);

        var result = await controller.RebuildChunks(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task UserCannotProcessOrViewAnotherUsersDocument()
    {
        await using var database = await ControllerTestDatabase.CreateAsync(
            DocumentStatus.Ready,
            includeText: true);
        var otherUserId = Guid.NewGuid();
        var controller = database.CreateController(otherUserId);

        var processResult = await controller.Process(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);
        var textResult = await controller.GetText(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);
        var chunksResult = await controller.GetChunks(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);
        var rebuildResult = await controller.RebuildChunks(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);
        var normalizationResult = await controller.RebuildNormalization(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(processResult);
        Assert.IsType<NotFoundObjectResult>(textResult.Result);
        Assert.IsType<NotFoundObjectResult>(chunksResult.Result);
        Assert.IsType<NotFoundObjectResult>(rebuildResult.Result);
        Assert.IsType<NotFoundObjectResult>(normalizationResult.Result);
        Assert.Empty(database.Queue.EnqueuedDocumentIds);
    }

    [Fact]
    public async Task OwnerCanRebuildNormalizationFromStoredRawText()
    {
        await using var database = await ControllerTestDatabase.CreateAsync(
            DocumentStatus.Ready,
            includeText: true);
        var controller = database.CreateController(database.OwnerId);

        var rebuild = await controller.RebuildNormalization(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);
        var normalizedText = await controller.GetText(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None,
            "normalized");

        Assert.IsType<OkObjectResult>(rebuild.Result);
        var sections = Assert.IsAssignableFrom<IReadOnlyList<ExtractedTextSectionResponse>>(
            Assert.IsType<OkObjectResult>(normalizedText.Result).Value);
        Assert.Single(sections);
        Assert.Equal("Protected text", sections[0].Content);
    }

    [Fact]
    public async Task NormalizedTextCannotBeReadBeforeNormalizationExists()
    {
        await using var database = await ControllerTestDatabase.CreateAsync(
            DocumentStatus.Ready,
            includeText: true);
        var controller = database.CreateController(database.OwnerId);

        var result = await controller.GetText(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None,
            "normalized");

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task NormalizationRebuildIsRejectedWhileProcessing()
    {
        await using var database = await ControllerTestDatabase.CreateAsync(
            DocumentStatus.Processing,
            includeText: true);
        var controller = database.CreateController(database.OwnerId);

        var result = await controller.RebuildNormalization(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task OwnerCanViewAndRebuildChunksFromStoredText()
    {
        await using var database = await ControllerTestDatabase.CreateAsync(
            DocumentStatus.Ready,
            includeText: true);
        var controller = database.CreateController(database.OwnerId);

        var before = await controller.GetChunks(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);
        var rebuild = await controller.RebuildChunks(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);
        var after = await controller.GetChunks(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);

        var originalChunks = Assert.IsAssignableFrom<IReadOnlyList<DocumentChunkResponse>>(
            Assert.IsType<OkObjectResult>(before.Result).Value);
        Assert.Single(originalChunks);
        Assert.IsType<OkObjectResult>(rebuild.Result);
        var rebuiltChunks = Assert.IsAssignableFrom<IReadOnlyList<DocumentChunkResponse>>(
            Assert.IsType<OkObjectResult>(after.Result).Value);
        Assert.Contains(rebuiltChunks, chunk => chunk.Content.Contains("Protected text", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TextCannotBeReadBeforeDocumentIsReady()
    {
        await using var database = await ControllerTestDatabase.CreateAsync(
            DocumentStatus.Uploaded);
        var controller = database.CreateController(database.OwnerId);

        var result = await controller.GetText(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task ManualRetryIsRejectedWhileDocumentIsProcessing()
    {
        await using var database = await ControllerTestDatabase.CreateAsync(
            DocumentStatus.Processing);
        var controller = database.CreateController(database.OwnerId);

        var result = await controller.Process(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Empty(database.Queue.EnqueuedDocumentIds);
    }

    private sealed class ControllerTestDatabase : IAsyncDisposable
    {
        private ControllerTestDatabase(
            ApplicationDbContext context,
            Guid ownerId,
            Guid projectId,
            Guid documentId)
        {
            Context = context;
            OwnerId = ownerId;
            ProjectId = projectId;
            DocumentId = documentId;
        }

        public ApplicationDbContext Context { get; }

        public Guid OwnerId { get; }

        public Guid ProjectId { get; }

        public Guid DocumentId { get; }

        public RecordingQueue Queue { get; } = new();

        public static async Task<ControllerTestDatabase> CreateAsync(
            DocumentStatus status = DocumentStatus.Uploaded,
            bool includeText = false)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"controller-tests-{Guid.NewGuid():N}")
                .Options;
            var context = new ApplicationDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var now = DateTime.UtcNow;
            var ownerId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var documentId = Guid.NewGuid();
            var owner = new ApplicationUser
            {
                Id = ownerId,
                UserName = "owner@example.com",
                NormalizedUserName = "OWNER@EXAMPLE.COM",
                Email = "owner@example.com",
                NormalizedEmail = "OWNER@EXAMPLE.COM",
                DisplayName = "Document Owner",
                CreatedAtUtc = now
            };
            var project = new Project
            {
                Id = projectId,
                Name = "Owned project",
                Owner = owner,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            var document = new StoredDocument
            {
                Id = documentId,
                Project = project,
                OriginalFileName = "owned.pdf",
                StoredFileName = $"{Guid.NewGuid():N}.pdf",
                ContentType = "application/pdf",
                FileSizeBytes = 100,
                Status = status,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                ExtractedSectionCount = includeText ? 1 : 0,
                ExtractedCharacterCount = includeText ? 14 : 0,
                ProcessedAtUtc = includeText ? now : null,
                ChunkCount = includeText ? 1 : 0,
                ChunkedAtUtc = includeText ? now : null
            };

            context.Documents.Add(document);

            if (includeText)
            {
                context.DocumentTextSections.Add(new DocumentTextSection
                {
                    Id = Guid.NewGuid(),
                    DocumentId = documentId,
                    SectionIndex = 0,
                    PageNumber = 1,
                    Content = "Protected text",
                    CreatedAtUtc = now
                });
                context.DocumentChunks.Add(new DocumentChunk
                {
                    Id = Guid.NewGuid(),
                    DocumentId = documentId,
                    ChunkIndex = 0,
                    Content = "Protected text",
                    CharacterCount = 14,
                    TokenCount = 2,
                    PageStart = 1,
                    PageEnd = 1,
                    SourceSectionStartIndex = 0,
                    SourceSectionEndIndex = 0,
                    CreatedAtUtc = now
                });
            }

            await context.SaveChangesAsync();

            return new ControllerTestDatabase(
                context,
                ownerId,
                projectId,
                documentId);
        }

        public DocumentsController CreateController(Guid? userId)
        {
            var claims = userId.HasValue
                ? new[] { new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()) }
                : [];
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            };

            var generator = new DocumentChunkGenerator(
                new Cl100kDocumentTokenizer(),
                Options.Create(new DocumentChunkingOptions()));
            var chunkingService = new DocumentChunkingService(
                Context,
                generator,
                NullLogger<DocumentChunkingService>.Instance);
            var normalizer = new DocumentTextNormalizer(
                Options.Create(new DocumentNormalizationOptions()));
            var normalizationService = new DocumentNormalizationService(
                Context,
                normalizer,
                generator,
                NullLogger<DocumentNormalizationService>.Instance);

            return new DocumentsController(
                Context,
                new StubFileStorage(),
                Queue,
                chunkingService,
                normalizationService,
                NullLogger<DocumentsController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext }
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
        }
    }

    private sealed class RecordingQueue : IDocumentProcessingQueue
    {
        public List<Guid> EnqueuedDocumentIds { get; } = [];

        public bool TryEnqueue(Guid documentId)
        {
            EnqueuedDocumentIds.Add(documentId);
            return true;
        }

        public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Complete(Guid documentId)
        {
        }
    }

    private sealed class StubFileStorage : IFileStorageService
    {
        public Task<string> SaveAsync(
            Stream source,
            string fileExtension,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<bool> ExistsAsync(string storedFileName, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<Stream> OpenReadAsync(
            string storedFileName,
            CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream());
    }
}
