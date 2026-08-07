using AI.DocumentAssistant.Server.Contracts;
using AI.DocumentAssistant.Server.Chunking;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Embeddings;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Normalization;
using AI.DocumentAssistant.Server.Processing;
using AI.DocumentAssistant.Server.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using StoredDocument = AI.DocumentAssistant.Server.Models.Document;

namespace AI.DocumentAssistant.Server.Tests;

public sealed class DocumentEmbeddingServiceTests
{
    [Fact]
    public async Task RebuildUsesExactPersistedContentInChunkOrderAndEmbedsLegacyChunks()
    {
        var chunks = new[]
        {
            (Index: 2, Content: "Third persisted chunk."),
            (Index: 0, Content: "\u0218tiin\u021B\u0103 \u0219i drept european."),
            (Index: 1, Content: "English text, plus \u4E16\u754C and e\u0301.")
        };
        await using var database = await EmbeddingTestDatabase.CreateAsync(chunks, hasEmbeddings: false);
        var provider = RecordingTextEmbeddingService.Successful(markerBase: 100);
        var service = database.CreateService(provider);

        var result = await service.RebuildAsync(database.DocumentId, CancellationToken.None);

        var call = Assert.Single(provider.Calls);
        Assert.Equal(chunks.OrderBy(chunk => chunk.Index).Select(chunk => chunk.Content), call.Texts);

        database.Context.ChangeTracker.Clear();
        var document = await database.Context.Documents.SingleAsync();
        var persistedChunks = await database.Context.DocumentChunks
            .OrderBy(chunk => chunk.ChunkIndex)
            .ToListAsync();

        Assert.Equal(DocumentStatus.Ready, document.Status);
        Assert.Equal(3, result.EmbeddedChunkCount);
        Assert.Equal(EmbeddingArchitecture.DefaultModel, result.EmbeddingModel);
        Assert.Equal(EmbeddingArchitecture.Dimensions, result.EmbeddingDimensions);
        Assert.Equal(result.EmbeddedAtUtc, document.EmbeddedAtUtc);
        Assert.Equal(3, document.EmbeddedChunkCount);
        Assert.Equal(EmbeddingArchitecture.DefaultModel, document.EmbeddingModel);
        Assert.Equal(EmbeddingArchitecture.Dimensions, document.EmbeddingDimensions);
        Assert.Null(document.EmbeddingError);

        for (var index = 0; index < persistedChunks.Count; index++)
        {
            var chunk = persistedChunks[index];
            Assert.Equal(chunks.Single(value => value.Index == chunk.ChunkIndex).Content, chunk.Content);
            Assert.NotNull(chunk.Embedding);
            var vector = chunk.Embedding!.ToArray();
            Assert.Equal(EmbeddingArchitecture.Dimensions, vector.Length);
            Assert.Equal(100 + index, vector[0]);
            Assert.Equal(EmbeddingArchitecture.DefaultModel, chunk.EmbeddingModel);
            Assert.Equal(EmbeddingArchitecture.Dimensions, chunk.EmbeddingDimensions);
            Assert.Equal(EmbeddingContentHasher.Compute(chunk.Content), chunk.EmbeddingContentHash);
            Assert.Equal(result.EmbeddedAtUtc, chunk.EmbeddedAtUtc);
        }
    }

    [Fact]
    public async Task ExplicitRebuildRegeneratesAlreadyCurrentEmbeddings()
    {
        await using var database = await EmbeddingTestDatabase.CreateAsync(
            [(Index: 0, Content: "Already embedded chunk.")],
            hasEmbeddings: true,
            oldMarkerBase: 7);
        var oldState = await database.ReadChunkStatesAsync();
        var provider = RecordingTextEmbeddingService.Successful(markerBase: 900);
        var service = database.CreateService(provider);

        await service.RebuildAsync(database.DocumentId, CancellationToken.None);

        Assert.Single(provider.Calls);
        database.Context.ChangeTracker.Clear();
        var chunk = await database.Context.DocumentChunks.SingleAsync();
        Assert.Equal(7, oldState[0].Vector![0]);
        Assert.Equal(900, chunk.Embedding!.ToArray()[0]);
        Assert.NotEqual(oldState[0].EmbeddedAtUtc, chunk.EmbeddedAtUtc);
    }

    [Fact]
    public async Task RebuildRejectsUnexpectedResultCountAndPreservesExistingEmbeddings()
    {
        await using var database = await EmbeddingTestDatabase.CreateAsync(
            [
                (Index: 0, Content: "First current chunk."),
                (Index: 1, Content: "Second current chunk.")
            ],
            hasEmbeddings: true);
        var oldChunks = await database.ReadChunkStatesAsync();
        var provider = new RecordingTextEmbeddingService((_, _) => Task.FromResult(
            new TextEmbeddingResult(
                EmbeddingArchitecture.DefaultModel,
                EmbeddingArchitecture.Dimensions,
                [CreateVector(EmbeddingArchitecture.Dimensions, 1)])));

        var exception = await Assert.ThrowsAsync<DocumentEmbeddingException>(() =>
            database.CreateService(provider).RebuildAsync(database.DocumentId, CancellationToken.None));

        Assert.Contains("result count", exception.SafeMessage, StringComparison.OrdinalIgnoreCase);
        await AssertFailurePreservedStateAsync(database, oldChunks, exception.SafeMessage);
    }

    [Fact]
    public async Task RebuildRejectsUnexpectedVectorDimensionsAndPreservesExistingEmbeddings()
    {
        await using var database = await EmbeddingTestDatabase.CreateAsync(
            [(Index: 0, Content: "Current chunk.")],
            hasEmbeddings: true);
        var oldChunks = await database.ReadChunkStatesAsync();
        var provider = new RecordingTextEmbeddingService((_, _) => Task.FromResult(
            new TextEmbeddingResult(
                EmbeddingArchitecture.DefaultModel,
                EmbeddingArchitecture.Dimensions,
                [CreateVector(EmbeddingArchitecture.Dimensions - 1, 1)])));

        var exception = await Assert.ThrowsAsync<DocumentEmbeddingException>(() =>
            database.CreateService(provider).RebuildAsync(database.DocumentId, CancellationToken.None));

        Assert.Contains("vector size", exception.SafeMessage, StringComparison.OrdinalIgnoreCase);
        await AssertFailurePreservedStateAsync(database, oldChunks, exception.SafeMessage);
    }

    [Fact]
    public async Task ProviderFailurePreservesEveryPreviousEmbeddingFieldAndStoresOnlySafeError()
    {
        await using var database = await EmbeddingTestDatabase.CreateAsync(
            [
                (Index: 0, Content: "First protected chunk."),
                (Index: 1, Content: "Second protected chunk.")
            ],
            hasEmbeddings: true);
        var oldChunks = await database.ReadChunkStatesAsync();
        var oldDocument = await database.ReadDocumentEmbeddingStateAsync();
        const string providerDetails = "provider-response-body-with-internal-details";
        var provider = new RecordingTextEmbeddingService((_, _) =>
            throw new InvalidOperationException(providerDetails));

        var exception = await Assert.ThrowsAsync<DocumentEmbeddingException>(() =>
            database.CreateService(provider).RebuildAsync(database.DocumentId, CancellationToken.None));

        const string safeMessage = "Document embeddings could not be generated. Please try again.";
        Assert.Equal(safeMessage, exception.SafeMessage);
        Assert.DoesNotContain(providerDetails, exception.SafeMessage, StringComparison.Ordinal);
        await AssertFailurePreservedStateAsync(database, oldChunks, safeMessage);

        var currentDocument = await database.ReadDocumentEmbeddingStateAsync();
        Assert.Equal(oldDocument.EmbeddedChunkCount, currentDocument.EmbeddedChunkCount);
        Assert.Equal(oldDocument.Model, currentDocument.Model);
        Assert.Equal(oldDocument.Dimensions, currentDocument.Dimensions);
        Assert.Equal(oldDocument.EmbeddedAtUtc, currentDocument.EmbeddedAtUtc);
    }

    [Fact]
    public async Task RebuildWithZeroChunksFailsSafelyWithoutCallingProvider()
    {
        await using var database = await EmbeddingTestDatabase.CreateAsync([], hasEmbeddings: false);
        var provider = RecordingTextEmbeddingService.Successful();

        var exception = await Assert.ThrowsAsync<DocumentEmbeddingException>(() =>
            database.CreateService(provider).RebuildAsync(database.DocumentId, CancellationToken.None));

        Assert.Contains("chunks are required", exception.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(provider.Calls);
        database.Context.ChangeTracker.Clear();
        var document = await database.Context.Documents.SingleAsync();
        Assert.Equal(DocumentStatus.Ready, document.Status);
        Assert.Equal(exception.SafeMessage, document.EmbeddingError);
        Assert.Equal(0, document.EmbeddedChunkCount);
    }

    [Fact]
    public async Task RebuildHonorsAndForwardsCancellationAndPreservesExistingEmbeddings()
    {
        await using var database = await EmbeddingTestDatabase.CreateAsync(
            [(Index: 0, Content: "Cancellable persisted chunk.")],
            hasEmbeddings: true);
        var oldChunks = await database.ReadChunkStatesAsync();
        var providerStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new RecordingTextEmbeddingService(async (_, cancellationToken) =>
        {
            providerStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        });
        using var cancellationSource = new CancellationTokenSource();

        var operation = database.CreateService(provider).RebuildAsync(
            database.DocumentId,
            cancellationSource.Token);
        await providerStarted.Task;
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        var call = Assert.Single(provider.Calls);
        Assert.True(call.CancellationToken.CanBeCanceled);
        Assert.True(call.CancellationToken.IsCancellationRequested);
        await AssertFailurePreservedStateAsync(
            database,
            oldChunks,
            "Embedding generation was interrupted. Please retry.");
    }

    [Fact]
    public void EfModelMapsNullablePgvectorColumnWithFixedDimensionsAndVectorExtension()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(npgsqlOptions => npgsqlOptions.UseVector())
            .Options;
        using var context = new ApplicationDbContext(options);
        var model = context.GetService<IDesignTimeModel>().Model;
        var entityType = model.FindEntityType(typeof(DocumentChunk));
        Assert.NotNull(entityType);

        var property = entityType!.FindProperty(nameof(DocumentChunk.Embedding));
        Assert.NotNull(property);
        Assert.Equal(typeof(Vector), property!.ClrType);
        Assert.True(property.IsNullable);
        Assert.Equal($"vector({EmbeddingArchitecture.Dimensions})", property.GetColumnType());
        Assert.Contains(
            model.GetAnnotations(),
            annotation => string.Equals(
                annotation.Name,
                "Npgsql:PostgresExtension:vector",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ApiContractsDoNotExposeVectorsOrFloatArrays()
    {
        var contractTypes = new[]
        {
            typeof(DocumentSummary),
            typeof(DocumentDetails),
            typeof(ExtractedTextSectionResponse),
            typeof(DocumentChunkResponse)
        };

        var exposedVectorProperties = contractTypes
            .SelectMany(type => type.GetProperties())
            .Where(property =>
                property.PropertyType == typeof(Vector) ||
                property.PropertyType == typeof(float[]) ||
                property.Name.Equals("Embedding", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("Vector", StringComparison.OrdinalIgnoreCase))
            .Select(property => $"{property.DeclaringType!.Name}.{property.Name}")
            .ToArray();

        Assert.Empty(exposedVectorProperties);
        Assert.DoesNotContain(
            typeof(DocumentChunkResponse).GetProperties(),
            property => property.Name.Contains("Embedding", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            contractTypes.SelectMany(type => type.GetProperties()),
            property => property.Name.Contains("ApiKey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RebuildServiceHasNoFileExtractionNormalizationOrChunkingDependencies()
    {
        var dependencyTypes = Assert.Single(typeof(DocumentEmbeddingService).GetConstructors())
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.DoesNotContain(typeof(IFileStorageService), dependencyTypes);
        Assert.DoesNotContain(typeof(IDocumentTextExtractor), dependencyTypes);
        Assert.DoesNotContain(typeof(IDocumentTextNormalizer), dependencyTypes);
        Assert.DoesNotContain(typeof(IDocumentChunkGenerator), dependencyTypes);
    }

    private static async Task AssertFailurePreservedStateAsync(
        EmbeddingTestDatabase database,
        IReadOnlyList<ChunkEmbeddingState> oldChunks,
        string expectedError)
    {
        var currentChunks = await database.ReadChunkStatesAsync();
        Assert.Equal(oldChunks.Count, currentChunks.Count);
        for (var index = 0; index < oldChunks.Count; index++)
        {
            Assert.Equal(oldChunks[index].Id, currentChunks[index].Id);
            Assert.Equal(oldChunks[index].Content, currentChunks[index].Content);
            Assert.Equal(oldChunks[index].Vector, currentChunks[index].Vector);
            Assert.Equal(oldChunks[index].Model, currentChunks[index].Model);
            Assert.Equal(oldChunks[index].Dimensions, currentChunks[index].Dimensions);
            Assert.Equal(oldChunks[index].ContentHash, currentChunks[index].ContentHash);
            Assert.Equal(oldChunks[index].EmbeddedAtUtc, currentChunks[index].EmbeddedAtUtc);
        }

        database.Context.ChangeTracker.Clear();
        var document = await database.Context.Documents.SingleAsync();
        Assert.Equal(DocumentStatus.Ready, document.Status);
        Assert.Equal(expectedError, document.EmbeddingError);
    }

    private static float[] CreateVector(int dimensions, float marker)
    {
        var vector = new float[dimensions];
        vector[0] = marker;
        return vector;
    }

    private sealed class RecordingTextEmbeddingService(
        Func<IReadOnlyList<string>, CancellationToken, Task<TextEmbeddingResult>> handler)
        : ITextEmbeddingService
    {
        public List<EmbeddingCall> Calls { get; } = [];

        public static RecordingTextEmbeddingService Successful(float markerBase = 1) =>
            new((texts, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new TextEmbeddingResult(
                    EmbeddingArchitecture.DefaultModel,
                    EmbeddingArchitecture.Dimensions,
                    texts.Select((_, index) =>
                        CreateVector(EmbeddingArchitecture.Dimensions, markerBase + index)).ToArray()));
            });

        public Task<TextEmbeddingResult> GenerateEmbeddingsAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken)
        {
            Calls.Add(new EmbeddingCall(texts.ToArray(), cancellationToken));
            return handler(texts, cancellationToken);
        }
    }

    private sealed record EmbeddingCall(
        IReadOnlyList<string> Texts,
        CancellationToken CancellationToken);

    private sealed record ChunkEmbeddingState(
        Guid Id,
        string Content,
        float[]? Vector,
        string? Model,
        int? Dimensions,
        string? ContentHash,
        DateTime? EmbeddedAtUtc);

    private sealed record DocumentEmbeddingState(
        int EmbeddedChunkCount,
        string? Model,
        int? Dimensions,
        DateTime? EmbeddedAtUtc);

    private sealed class EmbeddingTestDatabase : IAsyncDisposable
    {
        private EmbeddingTestDatabase(ApplicationDbContext context, Guid documentId)
        {
            Context = context;
            DocumentId = documentId;
        }

        public ApplicationDbContext Context { get; }

        public Guid DocumentId { get; }

        public static async Task<EmbeddingTestDatabase> CreateAsync(
            IReadOnlyList<(int Index, string Content)> chunkValues,
            bool hasEmbeddings,
            float oldMarkerBase = 7)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"embedding-service-tests-{Guid.NewGuid():N}")
                .Options;
            var context = new ApplicationDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var now = DateTime.UtcNow.AddMinutes(-10);
            var owner = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "embedding-owner@example.com",
                NormalizedUserName = "EMBEDDING-OWNER@EXAMPLE.COM",
                Email = "embedding-owner@example.com",
                NormalizedEmail = "EMBEDDING-OWNER@EXAMPLE.COM",
                DisplayName = "Embedding Owner",
                CreatedAtUtc = now
            };
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Owner = owner,
                Name = "Embedding project",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            var document = new StoredDocument
            {
                Id = Guid.NewGuid(),
                Project = project,
                OriginalFileName = "stored-document.pdf",
                StoredFileName = $"{Guid.NewGuid():N}.pdf",
                ContentType = "application/pdf",
                FileSizeBytes = 100,
                Status = DocumentStatus.Processing,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                ProcessedAtUtc = now,
                ChunkCount = chunkValues.Count,
                ChunkedAtUtc = now,
                EmbeddedChunkCount = hasEmbeddings ? chunkValues.Count : 0,
                EmbeddingModel = hasEmbeddings ? EmbeddingArchitecture.DefaultModel : null,
                EmbeddingDimensions = hasEmbeddings ? EmbeddingArchitecture.Dimensions : null,
                EmbeddedAtUtc = hasEmbeddings ? now : null
            };

            context.Documents.Add(document);
            foreach (var value in chunkValues)
            {
                context.DocumentChunks.Add(new DocumentChunk
                {
                    Id = Guid.NewGuid(),
                    DocumentId = document.Id,
                    ChunkIndex = value.Index,
                    Content = value.Content,
                    CharacterCount = value.Content.Length,
                    TokenCount = 10,
                    SourceSectionStartIndex = 0,
                    SourceSectionEndIndex = 0,
                    CreatedAtUtc = now,
                    Embedding = hasEmbeddings
                        ? new Vector(CreateVector(
                            EmbeddingArchitecture.Dimensions,
                            oldMarkerBase + value.Index))
                        : null,
                    EmbeddingModel = hasEmbeddings ? EmbeddingArchitecture.DefaultModel : null,
                    EmbeddingDimensions = hasEmbeddings ? EmbeddingArchitecture.Dimensions : null,
                    EmbeddingContentHash = hasEmbeddings
                        ? EmbeddingContentHasher.Compute(value.Content)
                        : null,
                    EmbeddedAtUtc = hasEmbeddings ? now : null
                });
            }

            await context.SaveChangesAsync();
            return new EmbeddingTestDatabase(context, document.Id);
        }

        public DocumentEmbeddingService CreateService(ITextEmbeddingService embeddingService) =>
            new(
                Context,
                embeddingService,
                Options.Create(new OpenAIEmbeddingOptions()),
                NullLogger<DocumentEmbeddingService>.Instance);

        public async Task<IReadOnlyList<ChunkEmbeddingState>> ReadChunkStatesAsync()
        {
            Context.ChangeTracker.Clear();
            return await Context.DocumentChunks
                .AsNoTracking()
                .OrderBy(chunk => chunk.ChunkIndex)
                .Select(chunk => new ChunkEmbeddingState(
                    chunk.Id,
                    chunk.Content,
                    chunk.Embedding == null ? null : chunk.Embedding.ToArray(),
                    chunk.EmbeddingModel,
                    chunk.EmbeddingDimensions,
                    chunk.EmbeddingContentHash,
                    chunk.EmbeddedAtUtc))
                .ToListAsync();
        }

        public async Task<DocumentEmbeddingState> ReadDocumentEmbeddingStateAsync()
        {
            Context.ChangeTracker.Clear();
            return await Context.Documents
                .AsNoTracking()
                .Select(document => new DocumentEmbeddingState(
                    document.EmbeddedChunkCount,
                    document.EmbeddingModel,
                    document.EmbeddingDimensions,
                    document.EmbeddedAtUtc))
                .SingleAsync();
        }

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
