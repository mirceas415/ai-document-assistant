using AI.DocumentAssistant.Server.Chunking;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Embeddings;
using AI.DocumentAssistant.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StoredDocument = AI.DocumentAssistant.Server.Models.Document;

namespace AI.DocumentAssistant.Server.Tests;

public sealed class DocumentChunkingServiceTests
{
    [Fact]
    public async Task RebuildReplacesChunksFromStoredSectionsAndUpdatesMetadata()
    {
        await using var database = await ChunkingTestDatabase.CreateAsync();
        var embeddingService = new DeterministicTextEmbeddingService();
        var service = database.CreateService(CreateGenerator(), embeddingService);

        var result = await service.RebuildAsync(
            database.DocumentId,
            CancellationToken.None);

        database.Context.ChangeTracker.Clear();
        var document = await database.Context.Documents.SingleAsync();
        var chunks = await database.Context.DocumentChunks
            .OrderBy(chunk => chunk.ChunkIndex)
            .ToListAsync();

        Assert.Equal(DocumentStatus.Ready, document.Status);
        Assert.Equal(result.ChunkCount, document.ChunkCount);
        Assert.Equal(result.ChunkedAtUtc, document.ChunkedAtUtc);
        Assert.Null(document.ChunkingError);
        Assert.Equal(chunks.Count, document.EmbeddedChunkCount);
        Assert.Equal(EmbeddingArchitecture.DefaultModel, document.EmbeddingModel);
        Assert.Equal(EmbeddingArchitecture.Dimensions, document.EmbeddingDimensions);
        Assert.NotNull(document.EmbeddedAtUtc);
        Assert.Null(document.EmbeddingError);
        Assert.DoesNotContain(chunks, chunk => chunk.Content == "Old chunk");
        Assert.Contains(chunks, chunk => chunk.Content.Contains(
            "Secțiune stocată cu diacritice",
            StringComparison.Ordinal));
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(chunk => chunk.ChunkIndex));
        Assert.All(chunks, AssertCompleteEmbedding);
        Assert.Single(embeddingService.Calls);
        Assert.Equal(chunks.Select(chunk => chunk.Content), embeddingService.Calls[0]);
    }

    [Fact]
    public async Task ChunkGenerationFailurePreservesPreviousChunksEmbeddingsAndSourceSections()
    {
        await using var database = await ChunkingTestDatabase.CreateAsync();
        var service = database.CreateService(new ThrowingGenerator());

        var exception = await Assert.ThrowsAsync<DocumentChunkingException>(
            () => service.RebuildAsync(database.DocumentId, CancellationToken.None));

        database.Context.ChangeTracker.Clear();
        var document = await database.Context.Documents.SingleAsync();
        var chunk = await database.Context.DocumentChunks.SingleAsync();

        Assert.Equal("Rebuild failed safely.", exception.SafeMessage);
        Assert.Equal(DocumentStatus.Ready, document.Status);
        Assert.Equal("Rebuild failed safely.", document.ChunkingError);
        Assert.Equal(1, document.ChunkCount);
        Assert.NotNull(document.ChunkedAtUtc);
        Assert.Equal(database.OldChunkId, chunk.Id);
        Assert.Equal("Old chunk", chunk.Content);
        AssertCompleteEmbedding(chunk);
        Assert.Equal(2, await database.Context.DocumentTextSections.CountAsync());
    }

    [Fact]
    public async Task EmbeddingFailurePreservesPreviousAuthoritativeChunksAndEmbeddings()
    {
        await using var database = await ChunkingTestDatabase.CreateAsync();
        var embeddingService = new DeterministicTextEmbeddingService { RemainingFailures = 1 };
        var service = database.CreateService(CreateGenerator(), embeddingService);

        var exception = await Assert.ThrowsAsync<DocumentEmbeddingException>(
            () => service.RebuildAsync(database.DocumentId, CancellationToken.None));

        database.Context.ChangeTracker.Clear();
        var document = await database.Context.Documents.SingleAsync();
        var chunk = await database.Context.DocumentChunks.SingleAsync();

        Assert.Equal("Document embeddings could not be generated. Please try again.", exception.SafeMessage);
        Assert.Equal(DocumentStatus.Ready, document.Status);
        Assert.Null(document.ChunkingError);
        Assert.Equal(exception.SafeMessage, document.EmbeddingError);
        Assert.Equal(database.OldChunkId, chunk.Id);
        Assert.Equal("Old chunk", chunk.Content);
        AssertCompleteEmbedding(chunk);
        Assert.Equal(1, document.EmbeddedChunkCount);
        Assert.Equal(EmbeddingArchitecture.DefaultModel, document.EmbeddingModel);
        Assert.Equal(EmbeddingArchitecture.Dimensions, document.EmbeddingDimensions);
        Assert.NotNull(document.EmbeddedAtUtc);
        Assert.Single(embeddingService.Calls);
    }

    private static void AssertCompleteEmbedding(DocumentChunk chunk)
    {
        Assert.NotNull(chunk.Embedding);
        Assert.Equal(EmbeddingArchitecture.Dimensions, chunk.Embedding!.ToArray().Length);
        Assert.Equal(EmbeddingArchitecture.DefaultModel, chunk.EmbeddingModel);
        Assert.Equal(EmbeddingArchitecture.Dimensions, chunk.EmbeddingDimensions);
        Assert.Equal(EmbeddingContentHasher.Compute(chunk.Content), chunk.EmbeddingContentHash);
        Assert.NotNull(chunk.EmbeddedAtUtc);
    }

    private static IDocumentChunkGenerator CreateGenerator() =>
        new DocumentChunkGenerator(
            new Cl100kDocumentTokenizer(),
            Options.Create(new DocumentChunkingOptions()));

    private sealed class ThrowingGenerator : IDocumentChunkGenerator
    {
        public IReadOnlyList<GeneratedDocumentChunk> Generate(
            IReadOnlyList<ChunkSourceSection> sourceSections,
            CancellationToken cancellationToken = default) =>
            throw new DocumentChunkingException("Rebuild failed safely.");
    }

    private sealed class ChunkingTestDatabase : IAsyncDisposable
    {
        private ChunkingTestDatabase(
            ApplicationDbContext context,
            Guid documentId,
            Guid oldChunkId)
        {
            Context = context;
            DocumentId = documentId;
            OldChunkId = oldChunkId;
        }

        public ApplicationDbContext Context { get; }

        public Guid DocumentId { get; }

        public Guid OldChunkId { get; }

        public static async Task<ChunkingTestDatabase> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"chunking-service-tests-{Guid.NewGuid():N}")
                .Options;
            var context = new ApplicationDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var now = DateTime.UtcNow;
            var documentId = Guid.NewGuid();
            var oldChunkId = Guid.NewGuid();
            var owner = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "chunks@example.com",
                NormalizedUserName = "CHUNKS@EXAMPLE.COM",
                Email = "chunks@example.com",
                NormalizedEmail = "CHUNKS@EXAMPLE.COM",
                DisplayName = "Chunk Owner",
                CreatedAtUtc = now
            };
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = "Chunk project",
                Owner = owner,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            var document = new StoredDocument
            {
                Id = documentId,
                Project = project,
                OriginalFileName = "stored.docx",
                StoredFileName = $"{Guid.NewGuid():N}.docx",
                ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                FileSizeBytes = 200,
                Status = DocumentStatus.Ready,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                ProcessedAtUtc = now,
                ExtractedSectionCount = 2,
                ExtractedCharacterCount = 83,
                ChunkCount = 1,
                ChunkedAtUtc = now,
                EmbeddedChunkCount = 1,
                EmbeddingModel = EmbeddingArchitecture.DefaultModel,
                EmbeddingDimensions = EmbeddingArchitecture.Dimensions,
                EmbeddedAtUtc = now
            };

            context.Documents.Add(document);
            context.DocumentTextSections.AddRange(
                new DocumentTextSection
                {
                    Id = Guid.NewGuid(),
                    DocumentId = documentId,
                    SectionIndex = 0,
                    Content = "Secțiune stocată cu diacritice: ă â î ș ț.",
                    PageNumber = 1,
                    SectionTitle = "Introducere",
                    CreatedAtUtc = now
                },
                new DocumentTextSection
                {
                    Id = Guid.NewGuid(),
                    DocumentId = documentId,
                    SectionIndex = 1,
                    Content = "Stored English section for rebuilding chunks.",
                    PageNumber = 2,
                    CreatedAtUtc = now
                });
            context.DocumentChunks.Add(new DocumentChunk
            {
                Id = oldChunkId,
                DocumentId = documentId,
                ChunkIndex = 0,
                Content = "Old chunk",
                CharacterCount = 9,
                TokenCount = 2,
                SourceSectionStartIndex = 0,
                SourceSectionEndIndex = 0,
                Embedding = new Pgvector.Vector(new float[EmbeddingArchitecture.Dimensions]),
                EmbeddingModel = EmbeddingArchitecture.DefaultModel,
                EmbeddingDimensions = EmbeddingArchitecture.Dimensions,
                EmbeddingContentHash = EmbeddingContentHasher.Compute("Old chunk"),
                EmbeddedAtUtc = now,
                CreatedAtUtc = now
            });

            await context.SaveChangesAsync();
            return new ChunkingTestDatabase(context, documentId, oldChunkId);
        }

        public DocumentChunkingService CreateService(
            IDocumentChunkGenerator generator,
            ITextEmbeddingService? embeddingService = null) =>
            new(
                Context,
                generator,
                embeddingService ?? new DeterministicTextEmbeddingService(),
                Options.Create(new OpenAIEmbeddingOptions()),
                NullLogger<DocumentChunkingService>.Instance);

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
