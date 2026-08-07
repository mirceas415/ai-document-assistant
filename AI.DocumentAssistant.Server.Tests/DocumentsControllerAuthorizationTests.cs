using System.Security.Claims;
using AI.DocumentAssistant.Server.Chunking;
using AI.DocumentAssistant.Server.Contracts;
using AI.DocumentAssistant.Server.Controllers;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Embeddings;
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
    public async Task UnauthenticatedEmbeddingRebuildIsRejected()
    {
        await using var database = await ControllerTestDatabase.CreateAsync(
            DocumentStatus.Ready,
            includeText: true);
        var controller = database.CreateController(null);

        var result = await controller.RebuildEmbeddings(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.Empty(database.EmbeddingService.Calls);
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
        var embeddingResult = await controller.RebuildEmbeddings(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(processResult);
        Assert.IsType<NotFoundObjectResult>(textResult.Result);
        Assert.IsType<NotFoundObjectResult>(chunksResult.Result);
        Assert.IsType<NotFoundObjectResult>(rebuildResult.Result);
        Assert.IsType<NotFoundObjectResult>(normalizationResult.Result);
        Assert.IsType<NotFoundObjectResult>(embeddingResult.Result);
        Assert.Empty(database.Queue.EnqueuedDocumentIds);
        Assert.Empty(database.EmbeddingService.Calls);
    }

    [Fact]
    public async Task OwnerCanGenerateEmbeddingsForLegacyChunksWithoutOpeningStoredFile()
    {
        await using var database = await ControllerTestDatabase.CreateAsync(
            DocumentStatus.Ready,
            includeText: true);
        var controller = database.CreateController(database.OwnerId);

        var before = await controller.GetById(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);
        var legacyDetails = Assert.IsType<DocumentDetails>(
            Assert.IsType<OkObjectResult>(before.Result).Value);
        Assert.False(legacyDetails.EmbeddingsAreCurrent);

        var result = await controller.RebuildEmbeddings(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var details = Assert.IsType<DocumentDetails>(response.Value);
        Assert.True(details.EmbeddingsAreCurrent);
        Assert.Equal(1, details.EmbeddedChunkCount);
        Assert.Equal(EmbeddingArchitecture.DefaultModel, details.EmbeddingModel);
        Assert.Equal(EmbeddingArchitecture.Dimensions, details.EmbeddingDimensions);
        Assert.NotNull(details.EmbeddedAtUtc);
        Assert.Equal(0, database.FileStorage.OpenReadCount);
        Assert.Equal(["Protected text"], Assert.Single(database.EmbeddingService.Calls));

        var chunk = await database.Context.DocumentChunks.SingleAsync();
        Assert.NotNull(chunk.Embedding);
        Assert.Equal(EmbeddingArchitecture.Dimensions, chunk.Embedding!.ToArray().Length);
        Assert.NotNull(chunk.EmbeddingContentHash);
    }

    [Fact]
    public async Task EmbeddingCurrencyDetectsChangedContentAndMissingVectorDespiteCurrentAggregates()
    {
        await using var database = await ControllerTestDatabase.CreateAsync(
            DocumentStatus.Ready,
            includeText: true);
        var controller = database.CreateController(database.OwnerId);
        var generated = await controller.RebuildEmbeddings(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(generated.Result);

        database.Context.ChangeTracker.Clear();
        var chunk = await database.Context.DocumentChunks.SingleAsync();
        chunk.Content = "Changed after embedding.";
        await database.Context.SaveChangesAsync();

        var changedContentResult = await controller.GetById(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);
        var changedContentDetails = Assert.IsType<DocumentDetails>(
            Assert.IsType<OkObjectResult>(changedContentResult.Result).Value);
        Assert.False(changedContentDetails.EmbeddingsAreCurrent);

        database.Context.ChangeTracker.Clear();
        chunk = await database.Context.DocumentChunks.SingleAsync();
        chunk.Content = "Protected text";
        chunk.Embedding = null;
        await database.Context.SaveChangesAsync();

        var missingVectorResult = await controller.GetAll(
            database.ProjectId,
            CancellationToken.None);
        var summaries = Assert.IsAssignableFrom<IReadOnlyList<DocumentSummary>>(
            Assert.IsType<OkObjectResult>(missingVectorResult.Result).Value);
        Assert.False(Assert.Single(summaries).EmbeddingsAreCurrent);
    }

    [Fact]
    public async Task ExplicitEmbeddingRebuildRegeneratesAlreadyValidEmbeddings()
    {
        await using var database = await ControllerTestDatabase.CreateAsync(
            DocumentStatus.Ready,
            includeText: true);
        var controller = database.CreateController(database.OwnerId);

        var first = await controller.RebuildEmbeddings(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);
        var second = await controller.RebuildEmbeddings(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(first.Result);
        Assert.IsType<OkObjectResult>(second.Result);
        Assert.Equal(2, database.EmbeddingService.Calls.Count);
    }

    [Fact]
    public async Task FailedEmbeddingRebuildPreservesPreviousEmbeddingsAndReturnsSafeError()
    {
        await using var database = await ControllerTestDatabase.CreateAsync(
            DocumentStatus.Ready,
            includeText: true);
        var controller = database.CreateController(database.OwnerId);

        var successful = await controller.RebuildEmbeddings(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(successful.Result);
        var previousChunk = await database.Context.DocumentChunks
            .AsNoTracking()
            .SingleAsync();
        var previousVector = previousChunk.Embedding!.ToArray();
        var previousHash = previousChunk.EmbeddingContentHash;
        database.EmbeddingService.ExceptionToThrow = new InvalidOperationException(
            "provider-internal-detail");

        var failed = await controller.RebuildEmbeddings(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);

        var errorResult = Assert.IsType<ObjectResult>(failed.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, errorResult.StatusCode);
        var error = Assert.IsType<ApiErrorResponse>(errorResult.Value);
        Assert.DoesNotContain("provider-internal-detail", error.Message, StringComparison.Ordinal);

        database.Context.ChangeTracker.Clear();
        var persistedChunk = await database.Context.DocumentChunks.SingleAsync();
        var persistedDocument = await database.Context.Documents.SingleAsync();
        Assert.Equal(previousVector, persistedChunk.Embedding!.ToArray());
        Assert.Equal(previousHash, persistedChunk.EmbeddingContentHash);
        Assert.Equal(DocumentStatus.Ready, persistedDocument.Status);
        Assert.DoesNotContain(
            "provider-internal-detail",
            persistedDocument.EmbeddingError,
            StringComparison.Ordinal);

        var detailsResult = await controller.GetById(
            database.ProjectId,
            database.DocumentId,
            CancellationToken.None);
        var details = Assert.IsType<DocumentDetails>(
            Assert.IsType<OkObjectResult>(detailsResult.Result).Value);
        Assert.True(details.EmbeddingsAreCurrent);
        Assert.Equal(persistedDocument.EmbeddingError, details.EmbeddingError);
    }

    [Fact]
    public async Task EmbeddingRebuildIsRejectedWhileProcessingOrWithoutChunks()
    {
        await using var processingDatabase = await ControllerTestDatabase.CreateAsync(
            DocumentStatus.Processing,
            includeText: true);
        var processingController = processingDatabase.CreateController(processingDatabase.OwnerId);
        var processingResult = await processingController.RebuildEmbeddings(
            processingDatabase.ProjectId,
            processingDatabase.DocumentId,
            CancellationToken.None);

        await using var emptyDatabase = await ControllerTestDatabase.CreateAsync(DocumentStatus.Ready);
        var emptyController = emptyDatabase.CreateController(emptyDatabase.OwnerId);
        var emptyResult = await emptyController.RebuildEmbeddings(
            emptyDatabase.ProjectId,
            emptyDatabase.DocumentId,
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(processingResult.Result);
        Assert.IsType<ConflictObjectResult>(emptyResult.Result);
        Assert.Empty(processingDatabase.EmbeddingService.Calls);
        Assert.Empty(emptyDatabase.EmbeddingService.Calls);
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

    [Fact]
    public async Task DocumentAndProjectDeletionAreRejectedWhileProcessing()
    {
        await using var documentDatabase = await ControllerTestDatabase.CreateAsync(
            DocumentStatus.Processing);
        var documentController = documentDatabase.CreateController(documentDatabase.OwnerId);

        var documentResult = await documentController.Delete(
            documentDatabase.ProjectId,
            documentDatabase.DocumentId,
            CancellationToken.None);

        await using var projectDatabase = await ControllerTestDatabase.CreateAsync(
            DocumentStatus.Processing);
        var projectController = projectDatabase.CreateProjectsController(projectDatabase.OwnerId);
        var projectResult = await projectController.Delete(
            projectDatabase.ProjectId,
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(documentResult);
        Assert.IsType<ConflictObjectResult>(projectResult);
        Assert.Equal(0, documentDatabase.FileStorage.DeleteCount);
        Assert.Equal(0, projectDatabase.FileStorage.DeleteCount);
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

        public RecordingEmbeddingService EmbeddingService { get; } = new();

        public StubFileStorage FileStorage { get; } = new();

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
            var embeddingOptions = Options.Create(new OpenAIEmbeddingOptions());
            var chunkingService = new DocumentChunkingService(
                Context,
                generator,
                EmbeddingService,
                embeddingOptions,
                NullLogger<DocumentChunkingService>.Instance);
            var normalizer = new DocumentTextNormalizer(
                Options.Create(new DocumentNormalizationOptions()));
            var normalizationService = new DocumentNormalizationService(
                Context,
                normalizer,
                generator,
                EmbeddingService,
                embeddingOptions,
                NullLogger<DocumentNormalizationService>.Instance);
            var documentEmbeddingService = new DocumentEmbeddingService(
                Context,
                EmbeddingService,
                embeddingOptions,
                NullLogger<DocumentEmbeddingService>.Instance);

            return new DocumentsController(
                Context,
                FileStorage,
                Queue,
                chunkingService,
                normalizationService,
                documentEmbeddingService,
                embeddingOptions,
                NullLogger<DocumentsController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext }
            };
        }

        public ProjectsController CreateProjectsController(Guid? userId)
        {
            var claims = userId.HasValue
                ? new[] { new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()) }
                : [];

            return new ProjectsController(Context, FileStorage)
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

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
        }
    }

    private sealed class RecordingEmbeddingService : ITextEmbeddingService
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Exception? ExceptionToThrow { get; set; }

        public Task<TextEmbeddingResult> GenerateEmbeddingsAsync(
            IReadOnlyList<string> inputs,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(inputs.ToArray());

            if (ExceptionToThrow is not null)
            {
                return Task.FromException<TextEmbeddingResult>(ExceptionToThrow);
            }

            var vectors = inputs
                .Select((_, index) =>
                {
                    var vector = new float[EmbeddingArchitecture.Dimensions];
                    vector[0] = index + 1;
                    return vector;
                })
                .ToArray();

            return Task.FromResult(new TextEmbeddingResult(
                EmbeddingArchitecture.DefaultModel,
                EmbeddingArchitecture.Dimensions,
                vectors));
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
        public int OpenReadCount { get; private set; }

        public int DeleteCount { get; private set; }

        public Task<string> SaveAsync(
            Stream source,
            string fileExtension,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string storedFileName, CancellationToken cancellationToken)
        {
            DeleteCount++;
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string storedFileName, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<Stream> OpenReadAsync(
            string storedFileName,
            CancellationToken cancellationToken)
        {
            OpenReadCount++;
            return Task.FromResult<Stream>(new MemoryStream());
        }
    }
}
