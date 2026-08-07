using AI.DocumentAssistant.Server.Chunking;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Embeddings;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Normalization;
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
        Assert.Equal(1, storedDocument.EmbeddedChunkCount);
        Assert.Equal(EmbeddingArchitecture.DefaultModel, storedDocument.EmbeddingModel);
        Assert.Equal(EmbeddingArchitecture.Dimensions, storedDocument.EmbeddingDimensions);
        Assert.NotNull(storedDocument.EmbeddedAtUtc);
        Assert.Null(storedDocument.EmbeddingError);
        Assert.Collection(
            storedSections,
            first =>
            {
                Assert.Equal("First section", first.Content);
                Assert.Equal("First section", first.NormalizedContent);
            },
            second =>
            {
                Assert.Equal("Second section", second.Content);
                Assert.Equal("Second section", second.NormalizedContent);
            });
        Assert.Single(storedChunks);
        Assert.Contains("First section", storedChunks[0].Content);
        Assert.Contains("Second section", storedChunks[0].Content);
        Assert.NotNull(storedChunks[0].Embedding);
        Assert.Equal(EmbeddingArchitecture.Dimensions, storedChunks[0].Embedding!.ToArray().Length);
        Assert.Equal(EmbeddingArchitecture.DefaultModel, storedChunks[0].EmbeddingModel);
        Assert.Equal(EmbeddingArchitecture.Dimensions, storedChunks[0].EmbeddingDimensions);
        Assert.Equal(
            EmbeddingContentHasher.Compute(storedChunks[0].Content),
            storedChunks[0].EmbeddingContentHash);
        Assert.NotNull(storedChunks[0].EmbeddedAtUtc);
        Assert.Equal(storedDocument.EmbeddedAtUtc, storedChunks[0].EmbeddedAtUtc);
    }

    [Fact]
    public async Task ProcessingNormalizesBeforeChunkingAndPreservesRawSections()
    {
        await using var database = await ProcessingTestDatabase.CreateAsync();
        var document = await database.AddDocumentAsync(DocumentStatus.Uploaded);
        var extractor = new StubExtractor([
            new ExtractedTextSection(0, "LEGAL HEADER\nMeaningful first page\nPage 1", PageNumber: 1),
            new ExtractedTextSection(1, "LEGAL HEADER\nMeaningful second page\nPage 2", PageNumber: 2),
            new ExtractedTextSection(2, "LEGAL HEADER\nMeaningful third page\nPage 3", PageNumber: 3)
        ]);

        await database.CreateService(extractor).ProcessAsync(document.Id, CancellationToken.None);

        database.Context.ChangeTracker.Clear();
        var storedDocument = await database.Context.Documents.SingleAsync();
        var sections = await database.Context.DocumentTextSections
            .OrderBy(section => section.SectionIndex)
            .ToListAsync();
        var chunks = await database.Context.DocumentChunks.ToListAsync();

        Assert.Equal(DocumentStatus.Ready, storedDocument.Status);
        Assert.NotNull(storedDocument.NormalizedAtUtc);
        Assert.Equal(3, storedDocument.NormalizationChangedSectionCount);
        Assert.True(storedDocument.NormalizationRemovedCharacterCount > 0);
        Assert.Equal("LEGAL HEADER\nMeaningful first page\nPage 1", sections[0].Content);
        Assert.Equal("Meaningful first page", sections[0].NormalizedContent);
        Assert.All(chunks, chunk =>
        {
            Assert.DoesNotContain("LEGAL HEADER", chunk.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("Page 1", chunk.Content, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task NormalizationFailureSetsFailedAndRetainsNoPartialReplacement()
    {
        await using var database = await ProcessingTestDatabase.CreateAsync();
        var document = await database.AddDocumentAsync(DocumentStatus.Uploaded);
        database.Context.DocumentTextSections.Add(new DocumentTextSection
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            SectionIndex = 0,
            Content = "Stale raw section",
            CreatedAtUtc = DateTime.UtcNow
        });
        database.Context.DocumentChunks.Add(new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            ChunkIndex = 0,
            Content = "Stale chunk",
            CharacterCount = 11,
            TokenCount = 2,
            SourceSectionStartIndex = 0,
            SourceSectionEndIndex = 0,
            CreatedAtUtc = DateTime.UtcNow
        });
        await database.Context.SaveChangesAsync();

        var service = database.CreateService(
            new ThrowingNormalizer(),
            new StubExtractor([new ExtractedTextSection(0, "New raw text", PageNumber: 1)]));

        await Assert.ThrowsAsync<DocumentNormalizationException>(
            () => service.ProcessAsync(document.Id, CancellationToken.None));

        database.Context.ChangeTracker.Clear();
        var storedDocument = await database.Context.Documents.SingleAsync();
        Assert.Equal(DocumentStatus.Failed, storedDocument.Status);
        Assert.Equal("Document normalization failed. Please retry.", storedDocument.NormalizationError);
        Assert.Empty(await database.Context.DocumentTextSections.ToListAsync());
        Assert.Empty(await database.Context.DocumentChunks.ToListAsync());
    }

    [Fact]
    public async Task ReprocessingReplacesRawNormalizedSectionsAndChunksTogether()
    {
        await using var database = await ProcessingTestDatabase.CreateAsync();
        var document = await database.AddDocumentAsync(DocumentStatus.Failed);
        database.Context.DocumentTextSections.Add(new DocumentTextSection
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            SectionIndex = 0,
            Content = "Old raw text",
            NormalizedContent = "Old normalized text",
            NormalizationChanged = true,
            RemovedCharacterCount = 4,
            NormalizedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        database.Context.DocumentChunks.Add(new DocumentChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            ChunkIndex = 0,
            Content = "Old chunk",
            CharacterCount = 9,
            TokenCount = 2,
            SourceSectionStartIndex = 0,
            SourceSectionEndIndex = 0,
            CreatedAtUtc = DateTime.UtcNow
        });
        await database.Context.SaveChangesAsync();

        await database.CreateService(new StubExtractor([
            new ExtractedTextSection(0, "New   raw text", PageNumber: 1)
        ])).ProcessAsync(document.Id, CancellationToken.None);

        database.Context.ChangeTracker.Clear();
        var sections = await database.Context.DocumentTextSections.ToListAsync();
        var chunks = await database.Context.DocumentChunks.ToListAsync();
        Assert.Single(sections);
        Assert.Equal("New   raw text", sections[0].Content);
        Assert.Equal("New raw text", sections[0].NormalizedContent);
        Assert.DoesNotContain(chunks, chunk => chunk.Content == "Old chunk");
        Assert.Contains(chunks, chunk => chunk.Content.Contains("New raw text", StringComparison.Ordinal));
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
    public async Task FailedChunkingRetainsNoPartialNewSectionsOrChunks()
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
        Assert.Equal(0, storedDocument.ExtractedSectionCount);
        Assert.Empty(await database.Context.DocumentTextSections.ToListAsync());
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

    [Fact]
    public async Task EmbeddingFailurePreventsReadyPersistsNoPartialStateKeepsFileAndRetrySucceeds()
    {
        await using var database = await ProcessingTestDatabase.CreateAsync();
        var document = await database.AddDocumentAsync(DocumentStatus.Uploaded);
        var embeddingService = new DeterministicTextEmbeddingService
        {
            RemainingFailures = 1,
            BeforeGenerateAsync = async cancellationToken =>
            {
                var statusDuringEmbedding = await database.Context.Documents
                    .Where(item => item.Id == document.Id)
                    .Select(item => item.Status)
                    .SingleAsync(cancellationToken);
                Assert.Equal(DocumentStatus.Processing, statusDuringEmbedding);
                Assert.Empty(await database.Context.DocumentChunks.ToListAsync(cancellationToken));
            }
        };
        var extractor = new StubExtractor([
            new ExtractedTextSection(0, "Text rom\u00e2nesc cu diacritice \u0219i English text.", PageNumber: 1)
        ]);
        var service = database.CreateService(embeddingService, extractor);

        var exception = await Assert.ThrowsAsync<DocumentEmbeddingException>(
            () => service.ProcessAsync(document.Id, CancellationToken.None));

        database.Context.ChangeTracker.Clear();
        var failedDocument = await database.Context.Documents.SingleAsync();
        Assert.Equal("Document embeddings could not be generated. Please try again.", exception.SafeMessage);
        Assert.Equal(DocumentStatus.Failed, failedDocument.Status);
        Assert.Equal(exception.SafeMessage, failedDocument.EmbeddingError);
        Assert.Equal(0, failedDocument.EmbeddedChunkCount);
        Assert.Null(failedDocument.EmbeddingModel);
        Assert.Null(failedDocument.EmbeddingDimensions);
        Assert.Null(failedDocument.EmbeddedAtUtc);
        Assert.Empty(await database.Context.DocumentTextSections.ToListAsync());
        Assert.Empty(await database.Context.DocumentChunks.ToListAsync());
        Assert.Equal(1, database.Storage.OpenCount);
        Assert.Equal(0, database.Storage.DeleteCount);

        await service.ProcessAsync(document.Id, CancellationToken.None);

        database.Context.ChangeTracker.Clear();
        var retriedDocument = await database.Context.Documents.SingleAsync();
        var retriedChunks = await database.Context.DocumentChunks.ToListAsync();
        Assert.Equal(DocumentStatus.Ready, retriedDocument.Status);
        Assert.Null(retriedDocument.EmbeddingError);
        Assert.Equal(retriedChunks.Count, retriedDocument.EmbeddedChunkCount);
        Assert.NotEmpty(retriedChunks);
        Assert.All(retriedChunks, AssertCompleteEmbedding);
        Assert.Equal(2, database.Storage.OpenCount);
        Assert.Equal(0, database.Storage.DeleteCount);
        Assert.Equal(2, embeddingService.Calls.Count);
        Assert.Equal(retriedChunks.Select(chunk => chunk.Content), embeddingService.Calls[1]);
    }

    [Fact]
    public async Task ProcessingPreservesChunkToEmbeddingAssociationForMultipleChunks()
    {
        await using var database = await ProcessingTestDatabase.CreateAsync();
        var document = await database.AddDocumentAsync(DocumentStatus.Uploaded);
        var generatedChunks = Enumerable.Range(0, 8)
            .Select(index => new GeneratedDocumentChunk(
                index,
                $"Deterministic chunk {index}",
                $"Deterministic chunk {index}".Length,
                3,
                PageStart: index + 1,
                PageEnd: index + 1,
                SectionTitle: null,
                SourceSectionStartIndex: 0,
                SourceSectionEndIndex: 0))
            .ToArray();
        var embeddingService = new DeterministicTextEmbeddingService();
        var service = database.CreateService(
            new FixedChunkGenerator(generatedChunks),
            embeddingService,
            new StubExtractor([new ExtractedTextSection(0, "Source text", PageNumber: 1)]));

        await service.ProcessAsync(document.Id, CancellationToken.None);

        database.Context.ChangeTracker.Clear();
        var storedDocument = await database.Context.Documents.SingleAsync();
        var storedChunks = await database.Context.DocumentChunks
            .OrderBy(chunk => chunk.ChunkIndex)
            .ToArrayAsync();

        Assert.Equal(DocumentStatus.Ready, storedDocument.Status);
        Assert.Equal(8, storedDocument.EmbeddedChunkCount);
        Assert.Single(embeddingService.Calls);
        Assert.Equal(generatedChunks.Select(chunk => chunk.Content), embeddingService.Calls[0]);
        Assert.Equal(generatedChunks.Select(chunk => chunk.Content), storedChunks.Select(chunk => chunk.Content));
        for (var index = 0; index < storedChunks.Length; index++)
        {
            Assert.Equal(index + 1, storedChunks[index].Embedding!.ToArray()[0]);
            AssertCompleteEmbedding(storedChunks[index]);
        }
    }

    [Fact]
    public async Task FailureAfterFirstEmbeddingBatchPersistsNoPartialAuthoritativeState()
    {
        await using var database = await ProcessingTestDatabase.CreateAsync();
        var document = await database.AddDocumentAsync(DocumentStatus.Uploaded);
        var generatedChunks = Enumerable.Range(0, 8)
            .Select(index => new GeneratedDocumentChunk(
                index,
                $"Batch failure chunk {index}",
                $"Batch failure chunk {index}".Length,
                4,
                PageStart: 1,
                PageEnd: 1,
                SectionTitle: null,
                SourceSectionStartIndex: 0,
                SourceSectionEndIndex: 0))
            .ToArray();
        var provider = new FailOnSecondBatchEmbeddingClient();
        var embeddingService = new OpenAITextEmbeddingService(
            provider,
            Options.Create(new OpenAIEmbeddingOptions { BatchSize = 3 }),
            NullLogger<OpenAITextEmbeddingService>.Instance);
        var service = database.CreateService(
            new FixedChunkGenerator(generatedChunks),
            embeddingService,
            new StubExtractor([new ExtractedTextSection(0, "Source text", PageNumber: 1)]));

        await Assert.ThrowsAsync<DocumentEmbeddingException>(() =>
            service.ProcessAsync(document.Id, CancellationToken.None));

        Assert.Equal([3, 3], provider.BatchSizes);
        database.Context.ChangeTracker.Clear();
        Assert.Equal(DocumentStatus.Failed, (await database.Context.Documents.SingleAsync()).Status);
        Assert.Empty(await database.Context.DocumentTextSections.ToListAsync());
        Assert.Empty(await database.Context.DocumentChunks.ToListAsync());
        Assert.Equal(0, database.Storage.DeleteCount);
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

    private sealed class ProcessingTestDatabase : IAsyncDisposable
    {
        private ProcessingTestDatabase(ApplicationDbContext context)
        {
            Context = context;
        }

        public ApplicationDbContext Context { get; }

        public StubFileStorage Storage { get; } = new();

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
            CreateService(CreateGenerator(), new DeterministicTextEmbeddingService(), extractors);

        public DocumentProcessingService CreateService(
            ITextEmbeddingService embeddingService,
            params IDocumentTextExtractor[] extractors) =>
            CreateService(CreateGenerator(), embeddingService, extractors);

        public DocumentProcessingService CreateService(
            IDocumentChunkGenerator generator,
            params IDocumentTextExtractor[] extractors)
            => CreateService(
                new DocumentTextNormalizer(Options.Create(new DocumentNormalizationOptions())),
                generator,
                new DeterministicTextEmbeddingService(),
                extractors);

        public DocumentProcessingService CreateService(
            IDocumentTextNormalizer normalizer,
            params IDocumentTextExtractor[] extractors)
            => CreateService(
                normalizer,
                CreateGenerator(),
                new DeterministicTextEmbeddingService(),
                extractors);

        public DocumentProcessingService CreateService(
            IDocumentChunkGenerator generator,
            ITextEmbeddingService embeddingService,
            params IDocumentTextExtractor[] extractors) =>
            CreateService(
                new DocumentTextNormalizer(Options.Create(new DocumentNormalizationOptions())),
                generator,
                embeddingService,
                extractors);

        private DocumentProcessingService CreateService(
            IDocumentTextNormalizer normalizer,
            IDocumentChunkGenerator generator,
            ITextEmbeddingService embeddingService,
            params IDocumentTextExtractor[] extractors)
        {
            return new DocumentProcessingService(
                Context,
                Storage,
                extractors,
                normalizer,
                generator,
                embeddingService,
                Options.Create(new OpenAIEmbeddingOptions()),
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

    private sealed class ThrowingNormalizer : IDocumentTextNormalizer
    {
        public DocumentNormalizationResult Normalize(
            IReadOnlyList<NormalizationSourceSection> sections,
            bool isPdf,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Synthetic normalization failure.");
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

    private sealed class FailOnSecondBatchEmbeddingClient : IOpenAIEmbeddingClient
    {
        public List<int> BatchSizes { get; } = [];

        public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
            IReadOnlyList<string> inputs,
            string model,
            int dimensions,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BatchSizes.Add(inputs.Count);
            if (BatchSizes.Count == 2)
            {
                throw new DocumentEmbeddingException(
                    "Document embeddings could not be generated. Please try again.");
            }

            return Task.FromResult<IReadOnlyList<float[]>>(inputs
                .Select((_, index) =>
                {
                    var vector = new float[dimensions];
                    vector[0] = index + 1;
                    return vector;
                })
                .ToArray());
        }
    }

    private sealed class FixedChunkGenerator(
        IReadOnlyList<GeneratedDocumentChunk> chunks) : IDocumentChunkGenerator
    {
        public IReadOnlyList<GeneratedDocumentChunk> Generate(
            IReadOnlyList<ChunkSourceSection> sourceSections,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return chunks;
        }
    }

    private sealed class StubFileStorage : IFileStorageService
    {
        public int DeleteCount { get; private set; }

        public int OpenCount { get; private set; }

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
            OpenCount++;
            return Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
        }
    }
}
