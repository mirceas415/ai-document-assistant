using AI.DocumentAssistant.Server.Chunking;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Embeddings;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Normalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StoredDocument = AI.DocumentAssistant.Server.Models.Document;

namespace AI.DocumentAssistant.Server.Tests;

public sealed class DocumentNormalizationServiceTests
{
    [Fact]
    public async Task RebuildUsesStoredRawSectionsPersistsNormalizationAndReplacesChunks()
    {
        await using var database = await NormalizationTestDatabase.CreateAsync();
        var embeddingService = new DeterministicTextEmbeddingService();
        var service = database.CreateService(embeddingService: embeddingService);

        var result = await service.RebuildAsync(database.DocumentId, CancellationToken.None);

        database.Context.ChangeTracker.Clear();
        var document = await database.Context.Documents.SingleAsync();
        var sections = await database.Context.DocumentTextSections
            .OrderBy(section => section.SectionIndex)
            .ToListAsync();
        var chunks = await database.Context.DocumentChunks.ToListAsync();

        Assert.Equal(DocumentStatus.Ready, document.Status);
        Assert.NotNull(document.NormalizedAtUtc);
        Assert.Equal(3, result.ChangedSectionCount);
        Assert.Equal(result.NormalizedCharacterCount, document.NormalizedCharacterCount);
        Assert.Equal("LEGAL HEADER\nFirst meaningful body\nPage 1", sections[0].Content);
        Assert.Equal("First meaningful body", sections[0].NormalizedContent);
        Assert.DoesNotContain(chunks, chunk => chunk.Content == "Old chunk");
        Assert.All(chunks, chunk =>
            Assert.DoesNotContain("LEGAL HEADER", chunk.Content, StringComparison.Ordinal));
        Assert.All(chunks, AssertCompleteEmbedding);
        Assert.Equal(chunks.Count, document.EmbeddedChunkCount);
        Assert.Equal(EmbeddingArchitecture.DefaultModel, document.EmbeddingModel);
        Assert.Equal(EmbeddingArchitecture.Dimensions, document.EmbeddingDimensions);
        Assert.NotNull(document.EmbeddedAtUtc);
        Assert.Null(document.EmbeddingError);
        Assert.Single(embeddingService.Calls);
        Assert.Equal(chunks.Select(chunk => chunk.Content), embeddingService.Calls[0]);
    }

    [Fact]
    public async Task NormalizationFailurePreservesPreviousNormalizationChunksAndEmbeddings()
    {
        await using var database = await NormalizationTestDatabase.CreateAsync();
        var service = database.CreateService(new ThrowingNormalizer());

        var exception = await Assert.ThrowsAsync<DocumentNormalizationException>(
            () => service.RebuildAsync(database.DocumentId, CancellationToken.None));

        database.Context.ChangeTracker.Clear();
        var document = await database.Context.Documents.SingleAsync();
        var sections = await database.Context.DocumentTextSections
            .OrderBy(section => section.SectionIndex)
            .ToListAsync();
        var chunk = await database.Context.DocumentChunks.SingleAsync();

        Assert.Equal("Document normalization failed. Please retry.", exception.SafeMessage);
        Assert.Equal(DocumentStatus.Ready, document.Status);
        Assert.Equal(exception.SafeMessage, document.NormalizationError);
        Assert.Equal("LEGAL HEADER\nFirst meaningful body\nPage 1", sections[0].Content);
        Assert.Equal(
            Enumerable.Range(0, 3).Select(index => $"Previously normalized {index}"),
            sections.Select(section => section.NormalizedContent));
        Assert.Equal(database.OldChunkId, chunk.Id);
        AssertCompleteEmbedding(chunk);
    }

    [Fact]
    public async Task RebuildIsDeterministicAndReplacesPreviousNormalizationAndChunks()
    {
        await using var database = await NormalizationTestDatabase.CreateAsync();
        var embeddingService = new DeterministicTextEmbeddingService();
        var service = database.CreateService(embeddingService: embeddingService);

        await service.RebuildAsync(database.DocumentId, CancellationToken.None);
        database.Context.ChangeTracker.Clear();
        var firstContent = await database.Context.DocumentTextSections
            .OrderBy(section => section.SectionIndex)
            .Select(section => section.NormalizedContent)
            .ToArrayAsync();
        var firstChunkIds = await database.Context.DocumentChunks
            .Select(chunk => chunk.Id)
            .ToArrayAsync();

        await service.RebuildAsync(database.DocumentId, CancellationToken.None);
        database.Context.ChangeTracker.Clear();
        var secondContent = await database.Context.DocumentTextSections
            .OrderBy(section => section.SectionIndex)
            .Select(section => section.NormalizedContent)
            .ToArrayAsync();
        var secondChunkIds = await database.Context.DocumentChunks
            .Select(chunk => chunk.Id)
            .ToArrayAsync();

        Assert.Equal(firstContent, secondContent);
        Assert.NotEmpty(firstChunkIds);
        Assert.DoesNotContain(secondChunkIds, id => firstChunkIds.Contains(id));
        Assert.Equal(2, embeddingService.Calls.Count);
        Assert.Equal(
            await database.Context.DocumentChunks.OrderBy(chunk => chunk.ChunkIndex).Select(chunk => chunk.Content).ToArrayAsync(),
            embeddingService.Calls[1]);
    }

    [Fact]
    public async Task EmbeddingFailurePreservesPreviousAuthoritativeNormalizationChunksAndEmbeddings()
    {
        await using var database = await NormalizationTestDatabase.CreateAsync();
        var embeddingService = new DeterministicTextEmbeddingService { RemainingFailures = 1 };
        var service = database.CreateService(embeddingService: embeddingService);

        var exception = await Assert.ThrowsAsync<DocumentEmbeddingException>(
            () => service.RebuildAsync(database.DocumentId, CancellationToken.None));

        database.Context.ChangeTracker.Clear();
        var document = await database.Context.Documents.SingleAsync();
        var sections = await database.Context.DocumentTextSections
            .OrderBy(section => section.SectionIndex)
            .ToListAsync();
        var chunk = await database.Context.DocumentChunks.SingleAsync();

        Assert.Equal("Document embeddings could not be generated. Please try again.", exception.SafeMessage);
        Assert.Equal(DocumentStatus.Ready, document.Status);
        Assert.Null(document.NormalizationError);
        Assert.Null(document.ChunkingError);
        Assert.Equal(exception.SafeMessage, document.EmbeddingError);
        Assert.Equal(
            Enumerable.Range(0, 3).Select(index => $"Previously normalized {index}"),
            sections.Select(section => section.NormalizedContent));
        Assert.Equal(database.OldChunkId, chunk.Id);
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

    private sealed class NormalizationTestDatabase : IAsyncDisposable
    {
        private NormalizationTestDatabase(
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

        public static async Task<NormalizationTestDatabase> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"normalization-tests-{Guid.NewGuid():N}")
                .Options;
            var context = new ApplicationDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var now = DateTime.UtcNow;
            var oldChunkId = Guid.NewGuid();
            var owner = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "normalization@example.com",
                NormalizedUserName = "NORMALIZATION@EXAMPLE.COM",
                Email = "normalization@example.com",
                NormalizedEmail = "NORMALIZATION@EXAMPLE.COM",
                DisplayName = "Normalization Owner",
                CreatedAtUtc = now
            };
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = "Normalization project",
                Owner = owner,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            var document = new StoredDocument
            {
                Id = Guid.NewGuid(),
                Project = project,
                OriginalFileName = "stored.pdf",
                StoredFileName = $"{Guid.NewGuid():N}.pdf",
                ContentType = "application/pdf",
                FileSizeBytes = 200,
                Status = DocumentStatus.Ready,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                ProcessedAtUtc = now,
                ExtractedSectionCount = 3,
                ExtractedCharacterCount = 132,
                NormalizedCharacterCount = 69,
                NormalizedAtUtc = now,
                ChunkCount = 1,
                ChunkedAtUtc = now,
                EmbeddedChunkCount = 1,
                EmbeddingModel = EmbeddingArchitecture.DefaultModel,
                EmbeddingDimensions = EmbeddingArchitecture.Dimensions,
                EmbeddedAtUtc = now
            };

            context.Documents.Add(document);
            var bodies = new[] { "First meaningful body", "Second meaningful body", "Third meaningful body" };
            context.DocumentTextSections.AddRange(bodies.Select((body, index) =>
                new DocumentTextSection
                {
                    Id = Guid.NewGuid(),
                    DocumentId = document.Id,
                    SectionIndex = index,
                    PageNumber = index + 1,
                    Content = $"LEGAL HEADER\n{body}\nPage {index + 1}",
                    NormalizedContent = $"Previously normalized {index}",
                    NormalizedAtUtc = now,
                    CreatedAtUtc = now
                }));
            context.DocumentChunks.Add(new DocumentChunk
            {
                Id = oldChunkId,
                DocumentId = document.Id,
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
            return new NormalizationTestDatabase(context, document.Id, oldChunkId);
        }

        public DocumentNormalizationService CreateService(
            IDocumentTextNormalizer? normalizer = null,
            ITextEmbeddingService? embeddingService = null)
        {
            var generator = new DocumentChunkGenerator(
                new Cl100kDocumentTokenizer(),
                Options.Create(new DocumentChunkingOptions()));
            return new DocumentNormalizationService(
                Context,
                normalizer ?? new DocumentTextNormalizer(
                    Options.Create(new DocumentNormalizationOptions())),
                generator,
                embeddingService ?? new DeterministicTextEmbeddingService(),
                new NoOpDocumentUnderstandingService(),
                Options.Create(new OpenAIEmbeddingOptions()),
                NullLogger<DocumentNormalizationService>.Instance);
        }

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }

    private sealed class ThrowingNormalizer : IDocumentTextNormalizer
    {
        public DocumentNormalizationResult Normalize(
            IReadOnlyList<NormalizationSourceSection> sections,
            bool isPdf,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Synthetic normalization failure.");
    }
}
