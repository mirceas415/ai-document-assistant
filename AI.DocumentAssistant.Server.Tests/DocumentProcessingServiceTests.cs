using AI.DocumentAssistant.Server.Chunking;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Processing;
using AI.DocumentAssistant.Server.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StoredDocument = AI.DocumentAssistant.Server.Models.Document;

namespace AI.DocumentAssistant.Server.Tests;

public sealed class DocumentProcessingServiceTests
{
    [Fact]
    public async Task SuccessfulProcessingChangesUploadedToReadyAndStoresCounts()
    {
        await using var database = await ProcessingTestDatabase.CreateAsync();
        var document = await database.AddDocumentAsync(DocumentStatus.Uploaded);
        var extractor = new StubExtractor([
            new ExtractedTextSection(0, "First section", PageNumber: 1),
            new ExtractedTextSection(1, "Second section", PageNumber: 2)
        ]);
        var service = database.CreateService(extractor);

        await service.ProcessAsync(document.Id, CancellationToken.None);

        database.Context.ChangeTracker.Clear();
        var storedDocument = await database.Context.Documents.SingleAsync();
        var storedSections = await database.Context.DocumentTextSections
            .OrderBy(section => section.SectionIndex)
            .ToListAsync();
        var storedChunks = await database.Context.DocumentChunks
            .OrderBy(chunk => chunk.ChunkIndex)
            .ToListAsync();

        Assert.Equal(DocumentStatus.Ready, storedDocument.Status);
        Assert.NotNull(storedDocument.ProcessingStartedAtUtc);
        Assert.NotNull(storedDocument.ProcessedAtUtc);
        Assert.Null(storedDocument.ProcessingError);
        Assert.Equal(2, storedDocument.ExtractedSectionCount);
        Assert.Equal(27, storedDocument.ExtractedCharacterCount);
        Assert.Equal(1, storedDocument.ChunkCount);
        Assert.NotNull(storedDocument.ChunkedAtUtc);
        Assert.Null(storedDocument.ChunkingError);
        Assert.Collection(
            storedSections,
            first => Assert.Equal("First section", first.Content),
            second => Assert.Equal("Second section", second.Content));
        Assert.Single(storedChunks);
        Assert.Contains("First section", storedChunks[0].Content);
        Assert.Contains("Second section", storedChunks[0].Content);
    }

    [Fact]
    public async Task FailedExtractionChangesStatusToFailedAndRemovesPartialSections()
    {
        await using var database = await ProcessingTestDatabase.CreateAsync();
        var document = await database.AddDocumentAsync(DocumentStatus.Uploaded);
        database.Context.DocumentTextSections.Add(new DocumentTextSection
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            SectionIndex = 0,
            Content = "Stale partial content",
            CreatedAtUtc = DateTime.UtcNow
        });
        database.Context.DocumentChunks.Add(new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            ChunkIndex = 0,
            Content = "Stale chunk",
            CharacterCount = 11,
            TokenCount = 3,
            SourceSectionStartIndex = 0,
            SourceSectionEndIndex = 0,
            CreatedAtUtc = DateTime.UtcNow
        });
        await database.Context.SaveChangesAsync();

        var extractor = new StubExtractor(
            new DocumentExtractionException("The test document could not be extracted."));
        var service = database.CreateService(extractor);

        await Assert.ThrowsAsync<DocumentExtractionException>(
            () => service.ProcessAsync(document.Id, CancellationToken.None));

        database.Context.ChangeTracker.Clear();
        var storedDocument = await database.Context.Documents.SingleAsync();

        Assert.Equal(DocumentStatus.Failed, storedDocument.Status);
        Assert.NotNull(storedDocument.ProcessingStartedAtUtc);
        Assert.Null(storedDocument.ProcessedAtUtc);
        Assert.Equal("The test document could not be extracted.", storedDocument.ProcessingError);
        Assert.Equal(0, storedDocument.ExtractedSectionCount);
        Assert.Equal(0, storedDocument.ExtractedCharacterCount);
        Assert.Empty(await database.Context.DocumentTextSections.ToListAsync());
        Assert.Empty(await database.Context.DocumentChunks.ToListAsync());
    }

    [Fact]
    public async Task FailedChunkingRetainsExtractedSectionsAndRemovesChunks()
    {
        await using var database = await ProcessingTestDatabase.CreateAsync();
        var document = await database.AddDocumentAsync(DocumentStatus.Uploaded);
        var extractor = new StubExtractor([
            new ExtractedTextSection(0, "Text extras în limba română.", PageNumber: 1)
        ]);
        var service = database.CreateService(
            new ThrowingChunkGenerator(),
            extractor);

        var exception = await Assert.ThrowsAsync<DocumentChunkingException>(
            () => service.ProcessAsync(document.Id, CancellationToken.None));

        database.Context.ChangeTracker.Clear();
        var storedDocument = await database.Context.Documents.SingleAsync();

        Assert.Equal("The test chunks could not be generated.", exception.SafeMessage);
        Assert.Equal(DocumentStatus.Failed, storedDocument.Status);
        Assert.Null(storedDocument.ProcessingError);
        Assert.Equal("The test chunks could not be generated.", storedDocument.ChunkingError);
        Assert.Equal(1, storedDocument.ExtractedSectionCount);
        Assert.Single(await database.Context.DocumentTextSections.ToListAsync());
        Assert.Empty(await database.Context.DocumentChunks.ToListAsync());
    }

    [Fact]
    public async Task UnsupportedDocumentTypeFailsWithSafeMessage()
    {
        await using var database = await ProcessingTestDatabase.CreateAsync();
        var document = await database.AddDocumentAsync(DocumentStatus.Uploaded);
        var service = database.CreateService();

        await Assert.ThrowsAsync<DocumentExtractionException>(
            () => service.ProcessAsync(document.Id, CancellationToken.None));

        database.Context.ChangeTracker.Clear();
        var storedDocument = await database.Context.Documents.SingleAsync();

        Assert.Equal(DocumentStatus.Failed, storedDocument.Status);
        Assert.Equal(
            "No text extractor is available for this document type.",
            storedDocument.ProcessingError);
    }

    private sealed class ProcessingTestDatabase : IAsyncDisposable
    {
        private ProcessingTestDatabase(ApplicationDbContext context)
        {
            Context = context;
        }

        public ApplicationDbContext Context { get; }

        public static async Task<ProcessingTestDatabase> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"processing-tests-{Guid.NewGuid():N}")
                .Options;
            var context = new ApplicationDbContext(options);
            await context.Database.EnsureCreatedAsync();

            return new ProcessingTestDatabase(context);
        }

        public async Task<StoredDocument> AddDocumentAsync(DocumentStatus status)
        {
            var now = DateTime.UtcNow;
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "processing@example.com",
                NormalizedUserName = "PROCESSING@EXAMPLE.COM",
                Email = "processing@example.com",
                NormalizedEmail = "PROCESSING@EXAMPLE.COM",
                DisplayName = "Processing Test",
                CreatedAtUtc = now
            };
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = "Processing tests",
                Owner = user,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            var document = new StoredDocument
            {
                Id = Guid.NewGuid(),
                Project = project,
                OriginalFileName = "test.pdf",
                StoredFileName = $"{Guid.NewGuid():N}.pdf",
                ContentType = "application/pdf",
                FileSizeBytes = 100,
                Status = status,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            Context.Documents.Add(document);
            await Context.SaveChangesAsync();
            return document;
        }

        public DocumentProcessingService CreateService(
            params IDocumentTextExtractor[] extractors) =>
            CreateService(CreateGenerator(), extractors);

        public DocumentProcessingService CreateService(
            IDocumentChunkGenerator generator,
            params IDocumentTextExtractor[] extractors)
        {
            var chunkingService = new DocumentChunkingService(
                Context,
                generator,
                NullLogger<DocumentChunkingService>.Instance);

            return new DocumentProcessingService(
                Context,
                new StubFileStorage(),
                extractors,
                chunkingService,
                NullLogger<DocumentProcessingService>.Instance);
        }

        private static IDocumentChunkGenerator CreateGenerator() =>
            new DocumentChunkGenerator(
                new Cl100kDocumentTokenizer(),
                Options.Create(new DocumentChunkingOptions()));

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
        }
    }

    private sealed class StubExtractor : IDocumentTextExtractor
    {
        private readonly IReadOnlyList<ExtractedTextSection>? _sections;
        private readonly Exception? _exception;

        public StubExtractor(IReadOnlyList<ExtractedTextSection> sections) =>
            _sections = sections;

        public StubExtractor(Exception exception) => _exception = exception;

        public bool CanProcess(string contentType, string fileExtension) => true;

        public Task<IReadOnlyList<ExtractedTextSection>> ExtractAsync(
            Stream documentStream,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return _exception is null
                ? Task.FromResult(_sections!)
                : Task.FromException<IReadOnlyList<ExtractedTextSection>>(_exception);
        }
    }

    private sealed class ThrowingChunkGenerator : IDocumentChunkGenerator
    {
        public IReadOnlyList<GeneratedDocumentChunk> Generate(
            IReadOnlyList<ChunkSourceSection> sourceSections,
            CancellationToken cancellationToken = default) =>
            throw new DocumentChunkingException(
                "The test chunks could not be generated.");
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
            Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
    }
}
