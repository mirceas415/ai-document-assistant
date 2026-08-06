using AI.DocumentAssistant.Server.Chunking;
using AI.DocumentAssistant.Server.Data;
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
        var service = database.CreateService(CreateGenerator());

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
        Assert.DoesNotContain(chunks, chunk => chunk.Content == "Old chunk");
        Assert.Contains(chunks, chunk => chunk.Content.Contains(
            "Secțiune stocată cu diacritice",
            StringComparison.Ordinal));
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(chunk => chunk.ChunkIndex));
    }

    [Fact]
    public async Task RebuildFailureRemovesPreviousChunksAndKeepsSourceSections()
    {
        await using var database = await ChunkingTestDatabase.CreateAsync();
        var service = database.CreateService(new ThrowingGenerator());

        var exception = await Assert.ThrowsAsync<DocumentChunkingException>(
            () => service.RebuildAsync(database.DocumentId, CancellationToken.None));

        database.Context.ChangeTracker.Clear();
        var document = await database.Context.Documents.SingleAsync();

        Assert.Equal("Rebuild failed safely.", exception.SafeMessage);
        Assert.Equal(DocumentStatus.Failed, document.Status);
        Assert.Equal("Rebuild failed safely.", document.ChunkingError);
        Assert.Equal(0, document.ChunkCount);
        Assert.Null(document.ChunkedAtUtc);
        Assert.Empty(await database.Context.DocumentChunks.ToListAsync());
        Assert.Equal(2, await database.Context.DocumentTextSections.CountAsync());
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
            Guid documentId)
        {
            Context = context;
            DocumentId = documentId;
        }

        public ApplicationDbContext Context { get; }

        public Guid DocumentId { get; }

        public static async Task<ChunkingTestDatabase> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"chunking-service-tests-{Guid.NewGuid():N}")
                .Options;
            var context = new ApplicationDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var now = DateTime.UtcNow;
            var documentId = Guid.NewGuid();
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
                ChunkedAtUtc = now
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
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                ChunkIndex = 0,
                Content = "Old chunk",
                CharacterCount = 9,
                TokenCount = 2,
                SourceSectionStartIndex = 0,
                SourceSectionEndIndex = 0,
                CreatedAtUtc = now
            });

            await context.SaveChangesAsync();
            return new ChunkingTestDatabase(context, documentId);
        }

        public DocumentChunkingService CreateService(IDocumentChunkGenerator generator) =>
            new(
                Context,
                generator,
                NullLogger<DocumentChunkingService>.Instance);

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
