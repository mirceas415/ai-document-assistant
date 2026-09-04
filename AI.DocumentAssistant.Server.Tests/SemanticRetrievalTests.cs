using System.Security.Claims;
using System.Text.Json;
using AI.DocumentAssistant.Server.Contracts;
using AI.DocumentAssistant.Server.Controllers;
using AI.DocumentAssistant.Server.Chunking;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Embeddings;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Retrieval;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pgvector;

namespace AI.DocumentAssistant.Server.Tests;

public sealed class SemanticRetrievalTests
{
    [Fact]
    public async Task SearchRejectsMissingAuthenticationClaim()
    {
        var retrieval = new RecordingRetrievalService();
        var controller = CreateController(retrieval, ownerId: null);

        var result = await controller.Search(
            Guid.NewGuid(),
            new SemanticSearchRequest { Query = "contract terms" },
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.Empty(retrieval.Calls);
    }

    [Fact]
    public async Task SearchReturnsNotFoundForAnotherUsersProjectWithoutEmbedding()
    {
        await using var fixture = await RetrievalFixture.CreateAsync();
        var otherOwnerId = Guid.NewGuid();
        var controller = CreateController(fixture.Service, otherOwnerId);

        var result = await controller.Search(
            fixture.ProjectId,
            new SemanticSearchRequest { Query = "private document" },
            CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Empty(fixture.EmbeddingService.Calls);
        Assert.Empty(fixture.ChunkSearch.Calls);
        Assert.Empty(fixture.LexicalSearch.Calls);
        Assert.Empty(fixture.MetadataSearch.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t\r\n")]
    public async Task SearchRejectsEmptyOrWhitespaceQuery(string? query)
    {
        var retrieval = new RecordingRetrievalService();
        var controller = CreateController(retrieval, Guid.NewGuid());

        var result = await controller.Search(
            Guid.NewGuid(),
            new SemanticSearchRequest { Query = query },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ApiErrorResponse>(badRequest.Value);
        Assert.Contains("query", error.Errors!.Keys);
        Assert.Empty(retrieval.Calls);
    }

    [Fact]
    public async Task SearchRejectsQueryLongerThanMaximum()
    {
        var retrieval = new RecordingRetrievalService();
        var controller = CreateController(retrieval, Guid.NewGuid());

        var result = await controller.Search(
            Guid.NewGuid(),
            new SemanticSearchRequest
            {
                Query = new string('x', SemanticRetrievalLimits.MaximumQueryLength + 1)
            },
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(retrieval.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(SemanticRetrievalLimits.MaximumTopK + 1)]
    public async Task SearchRejectsTopKOutsideBounds(int topK)
    {
        var retrieval = new RecordingRetrievalService();
        var controller = CreateController(retrieval, Guid.NewGuid());

        var result = await controller.Search(
            Guid.NewGuid(),
            new SemanticSearchRequest { Query = "valid", TopK = topK },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ApiErrorResponse>(badRequest.Value);
        Assert.Contains("topK", error.Errors!.Keys);
        Assert.Empty(retrieval.Calls);
    }

    [Fact]
    public async Task SearchUsesTrimmedQueryAndDefaultTopK()
    {
        var retrieval = new RecordingRetrievalService
        {
            Result = new SemanticRetrievalResult(
                SemanticRetrievalLimits.DefaultTopK,
                [])
        };
        var ownerId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var controller = CreateController(retrieval, ownerId);

        var result = await controller.Search(
            projectId,
            new SemanticSearchRequest { Query = "  Întrebare despre rezidență?  " },
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        var call = Assert.Single(retrieval.Calls);
        Assert.Equal(ownerId, call.OwnerId);
        Assert.Equal(projectId, call.ProjectId);
        Assert.Equal("Întrebare despre rezidență?", call.Query);
        Assert.Equal(SemanticRetrievalLimits.DefaultTopK, call.TopK);
    }

    [Fact]
    public async Task SearchResponseExtendsExistingChunkContractWithHybridDiagnostics()
    {
        var chunk = CreateChunk("Vodafone termination terms", 0.17) with
        {
            VectorRank = 4,
            LexicalRank = 1,
            MetadataDocumentRank = 1,
            LexicalRankScore = 0.81,
            FusedScore = 0.041,
            HybridRank = 4,
            RerankRank = 1,
            RerankRelevance = 4,
            MatchedMetadata =
            [
                new MatchedRetrievalMetadata("Organization", "Vodafone", false),
                new MatchedRetrievalMetadata("DocumentType", "Contract", false)
            ]
        };
        var retrieval = new RecordingRetrievalService
        {
            Result = new SemanticRetrievalResult(8, [chunk], true, false)
        };
        var controller = CreateController(retrieval, Guid.NewGuid());

        var action = await controller.Search(
            Guid.NewGuid(),
            new SemanticSearchRequest { Query = "Vodafone contract" },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<SemanticSearchResponse>(ok.Value);
        var result = Assert.Single(response.Results);
        Assert.Equal(0.17, result.CosineDistance);
        Assert.Equal(4, result.VectorRank);
        Assert.Equal(1, result.LexicalRank);
        Assert.Equal(1, result.MetadataDocumentRank);
        Assert.Equal(0.81, result.LexicalRankScore);
        Assert.Equal(0.041, result.FusedScore);
        Assert.Equal(4, result.HybridRank);
        Assert.Equal(1, result.RerankRank);
        Assert.Equal(4, result.RerankRelevance);
        Assert.Equal(2, result.MatchedMetadata!.Count);
        Assert.True(response.RerankingApplied);
        Assert.False(response.RerankingFallback);
    }

    [Fact]
    public async Task RetrievalEmbedsQueryExactlyOnceAndForwardsCurrentConfiguration()
    {
        await using var fixture = await RetrievalFixture.CreateAsync();

        var result = await fixture.Service.SearchAsync(
            fixture.OwnerId,
            fixture.ProjectId,
            "  Care sunt condițiile privind rezidența fiscală?  ",
            3,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(3, result.TopK);
        Assert.Equal(
            ["Care sunt condițiile privind rezidența fiscală?"],
            Assert.Single(fixture.EmbeddingService.Calls));
        var searchCall = Assert.Single(fixture.ChunkSearch.Calls);
        Assert.Equal(fixture.OwnerId, searchCall.OwnerId);
        Assert.Equal(fixture.ProjectId, searchCall.ProjectId);
        Assert.Equal(EmbeddingArchitecture.DefaultModel, searchCall.Model);
        Assert.Equal(EmbeddingArchitecture.Dimensions, searchCall.Dimensions);
        Assert.Equal(
            SemanticRetrievalLimits.DefaultVectorCandidateCount,
            searchCall.TopK);
        Assert.Equal(EmbeddingArchitecture.Dimensions, searchCall.QueryEmbedding.ToArray().Length);
        var lexicalCall = Assert.Single(fixture.LexicalSearch.Calls);
        Assert.Equal(
            SemanticRetrievalLimits.DefaultLexicalCandidateCount,
            lexicalCall.CandidateCount);
        Assert.Equal(
            "Care sunt condițiile privind rezidența fiscală?",
            lexicalCall.Query.OriginalText);
        Assert.Single(fixture.MetadataSearch.Calls);
    }

    [Fact]
    public async Task RetrievalCandidatePoolsAreClampedToCentralizedBounds()
    {
        await using var fixture = await RetrievalFixture.CreateAsync(
            hybridOptions: new HybridRetrievalOptions
            {
                VectorCandidateCount = 1_000,
                LexicalCandidateCount = 1_000,
                MetadataDocumentCandidateCount = 1_000
            });

        await fixture.Service.SearchAsync(
            fixture.OwnerId,
            fixture.ProjectId,
            "bounded candidates",
            SemanticRetrievalLimits.MaximumTopK,
            CancellationToken.None);

        Assert.Equal(
            SemanticRetrievalLimits.MaximumCandidateCount,
            Assert.Single(fixture.ChunkSearch.Calls).TopK);
        Assert.Equal(
            SemanticRetrievalLimits.MaximumCandidateCount,
            Assert.Single(fixture.LexicalSearch.Calls).CandidateCount);
        Assert.Equal(
            SemanticRetrievalLimits.MaximumMetadataDocumentCandidateCount,
            Assert.Single(fixture.MetadataSearch.Calls).CandidateCount);
    }

    [Theory]
    [InlineData("Care sunt condițiile privind rezidența fiscală? 📄")]
    [InlineData("What are the termination conditions? 📄")]
    public async Task RetrievalPreservesRomanianEnglishAndUnicode(string query)
    {
        await using var fixture = await RetrievalFixture.CreateAsync();
        const string chunkContent = "Română, English, 日本語 și emoji 📄 — preserved.";
        fixture.ChunkSearch.Results =
        [
            CreateChunk(chunkContent, distance: 0.12)
        ];

        var result = await fixture.Service.SearchAsync(
            fixture.OwnerId,
            fixture.ProjectId,
            query,
            8,
            CancellationToken.None);

        Assert.Equal([query], Assert.Single(fixture.EmbeddingService.Calls));
        Assert.Equal(chunkContent, Assert.Single(result!.Chunks).Content);
    }

    [Fact]
    public async Task RetrievalPreservesDatabaseOrderAndTopK()
    {
        await using var fixture = await RetrievalFixture.CreateAsync();
        fixture.ChunkSearch.Results =
        [
            CreateChunk("closest", 0.05),
            CreateChunk("second", 0.20),
            CreateChunk("third", 0.42)
        ];

        var result = await fixture.Service.SearchAsync(
            fixture.OwnerId,
            fixture.ProjectId,
            "rank these",
            2,
            CancellationToken.None);

        Assert.Equal(
            SemanticRetrievalLimits.DefaultVectorCandidateCount,
            Assert.Single(fixture.ChunkSearch.Calls).TopK);
        Assert.Equal(
            ["closest", "second"],
            result!.Chunks.Select(chunk => chunk.Content));
        Assert.Equal(
            [0.05, 0.20],
            result.Chunks.Select(chunk => chunk.CosineDistance));
    }

    [Theory]
    [InlineData("unexpected-model", EmbeddingArchitecture.Dimensions)]
    [InlineData(EmbeddingArchitecture.DefaultModel, EmbeddingArchitecture.Dimensions - 1)]
    public async Task RetrievalRejectsUnexpectedEmbeddingModelOrDimensions(
        string model,
        int dimensions)
    {
        await using var fixture = await RetrievalFixture.CreateAsync(model, dimensions);

        await Assert.ThrowsAsync<DocumentEmbeddingException>(() =>
            fixture.Service.SearchAsync(
                fixture.OwnerId,
                fixture.ProjectId,
                "configuration check",
                8,
                CancellationToken.None));

        Assert.Single(fixture.EmbeddingService.Calls);
        Assert.Empty(fixture.ChunkSearch.Calls);
        Assert.Empty(fixture.LexicalSearch.Calls);
        Assert.Empty(fixture.MetadataSearch.Calls);
    }

    [Fact]
    public async Task QueryEmbeddingIsNeverPersisted()
    {
        await using var fixture = await RetrievalFixture.CreateAsync();
        var beforeProjects = await fixture.Context.Projects.CountAsync();
        var beforeChunks = await fixture.Context.DocumentChunks.CountAsync();

        await fixture.Service.SearchAsync(
            fixture.OwnerId,
            fixture.ProjectId,
            "ephemeral query vector",
            8,
            CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();

        Assert.Equal(beforeProjects, await fixture.Context.Projects.CountAsync());
        Assert.Equal(beforeChunks, await fixture.Context.DocumentChunks.CountAsync());
        Assert.Empty(await fixture.Context.DocumentChunks.ToListAsync());
    }

    [Fact]
    public void PostgreSqlQueryFiltersOwnershipEligibilityAndStalenessBeforeCosineOrdering()
    {
        var sql = PgvectorSemanticChunkSearch.SearchSql;

        Assert.Contains("p.\"OwnerId\" = @owner_id", sql, StringComparison.Ordinal);
        Assert.Contains("p.\"Id\" = @project_id", sql, StringComparison.Ordinal);
        Assert.Contains("d.\"ProjectId\" = @project_id", sql, StringComparison.Ordinal);
        Assert.Contains("d.\"Status\" = @ready_status", sql, StringComparison.Ordinal);
        Assert.Contains("d.\"EmbeddedChunkCount\" = d.\"ChunkCount\"", sql, StringComparison.Ordinal);
        Assert.Contains("d.\"EmbeddingModel\" = @embedding_model", sql, StringComparison.Ordinal);
        Assert.Contains("d.\"EmbeddingDimensions\" = @embedding_dimensions", sql, StringComparison.Ordinal);
        Assert.Contains("c.\"Embedding\" IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("c.\"EmbeddingModel\" = @embedding_model", sql, StringComparison.Ordinal);
        Assert.Contains("c.\"EmbeddingDimensions\" = @embedding_dimensions", sql, StringComparison.Ordinal);
        Assert.Contains("c.\"EmbeddedAtUtc\" = d.\"EmbeddedAtUtc\"", sql, StringComparison.Ordinal);
        Assert.Contains("sha256(convert_to(c.\"Content\", 'UTF8'))", sql, StringComparison.Ordinal);
        Assert.Contains("c.\"Embedding\" <=> @query_embedding,", sql, StringComparison.Ordinal);
        Assert.Contains("d.\"Id\"", sql, StringComparison.Ordinal);
        Assert.Contains("c.\"ChunkIndex\"", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT @top_k", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchContractsCannotSerializeVectors()
    {
        var contractTypes = new[]
        {
            typeof(SemanticSearchRequest),
            typeof(SemanticSearchResponse),
            typeof(SemanticSearchResultResponse)
        };

        foreach (var property in contractTypes.SelectMany(type => type.GetProperties()))
        {
            Assert.NotEqual(typeof(Vector), property.PropertyType);
            Assert.NotEqual(typeof(float[]), property.PropertyType);
            Assert.DoesNotContain(
                "embedding",
                property.Name,
                StringComparison.OrdinalIgnoreCase);
        }

        var response = new SemanticSearchResponse(
            8,
            [new SemanticSearchResultResponse(
                Guid.NewGuid(),
                "unicode-șță.pdf",
                Guid.NewGuid(),
                0,
                "Safe source text.",
                1,
                1,
                null,
                0.1)]);
        var json = JsonSerializer.Serialize(response);

        Assert.DoesNotContain("embedding", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storage", json, StringComparison.OrdinalIgnoreCase);
    }

    private static ProjectIntelligenceController CreateController(
        ISemanticRetrievalService retrievalService,
        Guid? ownerId)
    {
        var controller = new ProjectIntelligenceController(retrievalService);
        var claims = ownerId is null
            ? Array.Empty<Claim>()
            : [new Claim(ClaimTypes.NameIdentifier, ownerId.Value.ToString())];
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
        return controller;
    }

    private static RetrievedDocumentChunk CreateChunk(string content, double distance) =>
        new(
            Guid.NewGuid(),
            "document.pdf",
            Guid.NewGuid(),
            0,
            content,
            1,
            1,
            null,
            distance);

    private sealed class RecordingRetrievalService : ISemanticRetrievalService
    {
        public List<RetrievalCall> Calls { get; } = [];

        public SemanticRetrievalResult? Result { get; init; }

        public Task<SemanticRetrievalResult?> SearchAsync(
            Guid ownerId,
            Guid projectId,
            string query,
            int topK,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(new RetrievalCall(ownerId, projectId, query, topK));
            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingEmbeddingService : ITextEmbeddingService
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public string Model { get; init; } = EmbeddingArchitecture.DefaultModel;

        public int Dimensions { get; init; } = EmbeddingArchitecture.Dimensions;

        public Task<TextEmbeddingResult> GenerateEmbeddingsAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(texts.ToArray());
            var vector = new float[Dimensions];
            vector[0] = 1;
            return Task.FromResult(new TextEmbeddingResult(Model, Dimensions, [vector]));
        }
    }

    private sealed class RecordingChunkSearch : ISemanticChunkSearch
    {
        public List<ChunkSearchCall> Calls { get; } = [];

        public IReadOnlyList<RetrievedDocumentChunk> Results { get; set; } = [];

        public Task<IReadOnlyList<RetrievedDocumentChunk>> SearchAsync(
            Guid ownerId,
            Guid projectId,
            Vector queryEmbedding,
            string embeddingModel,
            int embeddingDimensions,
            int topK,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(new ChunkSearchCall(
                ownerId,
                projectId,
                queryEmbedding,
                embeddingModel,
                embeddingDimensions,
                topK));
            return Task.FromResult(Results);
        }
    }

    private sealed class RecordingLexicalSearch : ILexicalChunkSearch
    {
        public List<LexicalSearchCall> Calls { get; } = [];

        public IReadOnlyList<RetrievedDocumentChunk> Results { get; set; } = [];

        public Task<IReadOnlyList<RetrievedDocumentChunk>> SearchAsync(
            Guid ownerId,
            Guid projectId,
            RetrievalQuery query,
            int candidateCount,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(new LexicalSearchCall(
                ownerId,
                projectId,
                query,
                candidateCount));
            return Task.FromResult(Results);
        }
    }

    private sealed class RecordingMetadataSearch : IMetadataDocumentSearch
    {
        public List<MetadataSearchCall> Calls { get; } = [];

        public IReadOnlyList<MetadataDocumentMatch> Results { get; set; } = [];

        public Task<IReadOnlyList<MetadataDocumentMatch>> SearchAsync(
            Guid ownerId,
            Guid projectId,
            RetrievalQuery query,
            int candidateCount,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(new MetadataSearchCall(
                ownerId,
                projectId,
                query,
                candidateCount));
            return Task.FromResult(Results);
        }
    }

    private sealed class RetrievalFixture : IAsyncDisposable
    {
        private RetrievalFixture(
            ApplicationDbContext context,
            Guid ownerId,
            Guid projectId,
            RecordingEmbeddingService embeddingService,
            RecordingChunkSearch chunkSearch,
            RecordingLexicalSearch lexicalSearch,
            RecordingMetadataSearch metadataSearch,
            SemanticRetrievalService service)
        {
            Context = context;
            OwnerId = ownerId;
            ProjectId = projectId;
            EmbeddingService = embeddingService;
            ChunkSearch = chunkSearch;
            LexicalSearch = lexicalSearch;
            MetadataSearch = metadataSearch;
            Service = service;
        }

        public ApplicationDbContext Context { get; }

        public Guid OwnerId { get; }

        public Guid ProjectId { get; }

        public RecordingEmbeddingService EmbeddingService { get; }

        public RecordingChunkSearch ChunkSearch { get; }

        public RecordingLexicalSearch LexicalSearch { get; }

        public RecordingMetadataSearch MetadataSearch { get; }

        public SemanticRetrievalService Service { get; }

        public static async Task<RetrievalFixture> CreateAsync(
            string resultModel = EmbeddingArchitecture.DefaultModel,
            int resultDimensions = EmbeddingArchitecture.Dimensions,
            HybridRetrievalOptions? hybridOptions = null)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"semantic-retrieval-{Guid.NewGuid():N}")
                .Options;
            var context = new ApplicationDbContext(options);
            var ownerId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            context.Projects.Add(new Project
            {
                Id = projectId,
                OwnerId = ownerId,
                Name = "Retrieval project",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await context.SaveChangesAsync();

            var embeddingService = new RecordingEmbeddingService
            {
                Model = resultModel,
                Dimensions = resultDimensions
            };
            var chunkSearch = new RecordingChunkSearch();
            var lexicalSearch = new RecordingLexicalSearch();
            var metadataSearch = new RecordingMetadataSearch();
            var configuredHybridOptions = Options.Create(
                hybridOptions ?? new HybridRetrievalOptions());
            var configuredRerankingOptions = Options.Create(
                new RetrievalRerankingOptions());
            var service = new SemanticRetrievalService(
                context,
                embeddingService,
                chunkSearch,
                lexicalSearch,
                metadataSearch,
                new DeterministicRetrievalQueryAnalyzer(),
                new ReciprocalRankFusion(configuredHybridOptions),
                new RetrievalRerankingInputBuilder(
                    new Cl100kDocumentTokenizer(),
                    configuredRerankingOptions),
                new PassThroughRetrievalReranker(),
                Options.Create(new OpenAIEmbeddingOptions
                {
                    EmbeddingModel = EmbeddingArchitecture.DefaultModel,
                    EmbeddingDimensions = EmbeddingArchitecture.Dimensions,
                    BatchSize = 32
                }),
                configuredHybridOptions,
                configuredRerankingOptions,
                NullLogger<SemanticRetrievalService>.Instance);

            return new RetrievalFixture(
                context,
                ownerId,
                projectId,
                embeddingService,
                chunkSearch,
                lexicalSearch,
                metadataSearch,
                service);
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed record RetrievalCall(
        Guid OwnerId,
        Guid ProjectId,
        string Query,
        int TopK);

    private sealed record ChunkSearchCall(
        Guid OwnerId,
        Guid ProjectId,
        Vector QueryEmbedding,
        string Model,
        int Dimensions,
        int TopK);

    private sealed record LexicalSearchCall(
        Guid OwnerId,
        Guid ProjectId,
        RetrievalQuery Query,
        int CandidateCount);

    private sealed record MetadataSearchCall(
        Guid OwnerId,
        Guid ProjectId,
        RetrievalQuery Query,
        int CandidateCount);
}
