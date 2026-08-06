using AI.DocumentAssistant.Server.Chunking;
using AI.DocumentAssistant.Server.Data;
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
        var service = database.CreateService();

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
    }

    [Fact]
    public async Task RebuildFailureKeepsRawContentButRemovesPartialNormalizedDataAndChunks()
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

        Assert.Equal("Document normalization failed. Please retry.", exception.SafeMessage);
        Assert.Equal(DocumentStatus.Failed, document.Status);
        Assert.Equal(exception.SafeMessage, document.NormalizationError);
        Assert.Equal("LEGAL HEADER\nFirst meaningful body\nPage 1", sections[0].Content);
        Assert.All(sections, section => Assert.Null(section.NormalizedContent));
        Assert.Empty(await database.Context.DocumentChunks.ToListAsync());
    }

    [Fact]
    public async Task RebuildIsDeterministicAndReplacesPreviousNormalizationAndChunks()
    {
        await using var database = await NormalizationTestDatabase.CreateAsync();
        var service = database.CreateService();

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
    }

    private sealed class NormalizationTestDatabase : IAsyncDisposable
    {
        private NormalizationTestDatabase(ApplicationDbContext context, Guid documentId)
        {
            Context = context;
            DocumentId = documentId;
        }

        public ApplicationDbContext Context { get; }

        public Guid DocumentId { get; }

        public static async Task<NormalizationTestDatabase> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"normalization-tests-{Guid.NewGuid():N}")
                .Options;
            var context = new ApplicationDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var now = DateTime.UtcNow;
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
                ChunkCount = 1,
                ChunkedAtUtc = now
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
                    CreatedAtUtc = now
                }));
            context.DocumentChunks.Add(new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
                ChunkIndex = 0,
                Content = "Old chunk",
                CharacterCount = 9,
                TokenCount = 2,
                SourceSectionStartIndex = 0,
                SourceSectionEndIndex = 0,
                CreatedAtUtc = now
            });

            await context.SaveChangesAsync();
            return new NormalizationTestDatabase(context, document.Id);
        }

        public DocumentNormalizationService CreateService(IDocumentTextNormalizer? normalizer = null)
        {
            var generator = new DocumentChunkGenerator(
                new Cl100kDocumentTokenizer(),
                Options.Create(new DocumentChunkingOptions()));
            return new DocumentNormalizationService(
                Context,
                normalizer ?? new DocumentTextNormalizer(
                    Options.Create(new DocumentNormalizationOptions())),
                generator,
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
