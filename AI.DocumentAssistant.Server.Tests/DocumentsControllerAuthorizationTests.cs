using System.Security.Claims;
using AI.DocumentAssistant.Server.Controllers;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Processing;
using AI.DocumentAssistant.Server.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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

        Assert.IsType<NotFoundObjectResult>(processResult);
        Assert.IsType<NotFoundObjectResult>(textResult.Result);
        Assert.Empty(database.Queue.EnqueuedDocumentIds);
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
                ProcessedAtUtc = includeText ? now : null
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

            return new DocumentsController(
                Context,
                new StubFileStorage(),
                Queue,
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
