using System.Security.Claims;
using AI.DocumentAssistant.Server.Chunking;
using AI.DocumentAssistant.Server.Contracts;
using AI.DocumentAssistant.Server.Controllers;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Embeddings;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Rag;
using AI.DocumentAssistant.Server.Retrieval;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pgvector;

namespace AI.DocumentAssistant.Server.Tests;

public sealed class RetrievalRerankingTests
{
    [Fact]
    public async Task RerankingReordersCandidatesBeforeFinalTopKAndPreservesHybridRanks()
    {
        var reranker = new RecordingReranker
        {
            Handler = (_, _) => Task.FromResult(Result(
                ("C3", 4),
                ("C1", 3),
                ("C2", 1)))
        };
        await using var fixture = await RerankingFixture.CreateAsync(reranker: reranker);
        var first = Chunk("hybrid first");
        var second = Chunk("hybrid second");
        var bestEvidence = Chunk("actual answer evidence");
        fixture.VectorResults = [first, second, bestEvidence];

        var result = await fixture.Service.SearchAsync(
            fixture.OwnerId,
            fixture.ProjectId,
            "Which candidate actually answers the question?",
            2,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.RerankingApplied);
        Assert.False(result.RerankingFallback);
        Assert.Equal(
            [bestEvidence.ChunkId, first.ChunkId],
            result.Chunks.Select(candidate => candidate.ChunkId));
        Assert.Equal([3, 1], result.Chunks.Select(candidate => candidate.HybridRank));
        Assert.Equal([1, 2], result.Chunks.Select(candidate => candidate.RerankRank));
        Assert.Equal([4, 3], result.Chunks.Select(candidate => candidate.RerankRelevance));
        var call = Assert.Single(reranker.Calls);
        Assert.Equal(
            ["C1", "C2", "C3"],
            call.Candidates.Select(candidate => candidate.CandidateId));
        Assert.Equal(
            [first.ChunkId, second.ChunkId, bestEvidence.ChunkId],
            call.Candidates.Select(candidate => candidate.ChunkId));
    }

    [Fact]
    public async Task DisabledRerankingPreservesM13OrderWithoutCallingProvider()
    {
        var reranker = new RecordingReranker();
        await using var fixture = await RerankingFixture.CreateAsync(
            reranker,
            new RetrievalRerankingOptions { Enabled = false });
        var first = Chunk("first");
        var second = Chunk("second");
        fixture.VectorResults = [first, second];

        var result = await fixture.Service.SearchAsync(
            fixture.OwnerId,
            fixture.ProjectId,
            "preserve hybrid order",
            1,
            CancellationToken.None);

        Assert.Equal(first.ChunkId, Assert.Single(result!.Chunks).ChunkId);
        Assert.False(result.RerankingApplied);
        Assert.False(result.RerankingFallback);
        Assert.Empty(reranker.Calls);
    }

    [Fact]
    public async Task ZeroAndOneCandidateSkipProvider()
    {
        var reranker = new RecordingReranker();
        await using var fixture = await RerankingFixture.CreateAsync(reranker: reranker);

        var empty = await fixture.Service.SearchAsync(
            fixture.OwnerId,
            fixture.ProjectId,
            "nothing eligible",
            1,
            CancellationToken.None);
        fixture.VectorResults = [Chunk("only candidate")];
        var single = await fixture.Service.SearchAsync(
            fixture.OwnerId,
            fixture.ProjectId,
            "one result",
            1,
            CancellationToken.None);

        Assert.Empty(empty!.Chunks);
        Assert.Single(single!.Chunks);
        Assert.Empty(reranker.Calls);
    }

    [Fact]
    public async Task CandidateCountAtOrBelowRequestedTopKSkipsProvider()
    {
        var reranker = new RecordingReranker();
        await using var fixture = await RerankingFixture.CreateAsync(reranker: reranker);
        fixture.VectorResults = [Chunk("one"), Chunk("two"), Chunk("three")];

        var result = await fixture.Service.SearchAsync(
            fixture.OwnerId,
            fixture.ProjectId,
            "all candidates fit",
            3,
            CancellationToken.None);

        Assert.Equal(3, result!.Chunks.Count);
        Assert.False(result.RerankingApplied);
        Assert.Empty(reranker.Calls);
    }

    [Fact]
    public async Task ProviderFailureFallsBackCompletelyToHybridOrder()
    {
        var reranker = new RecordingReranker
        {
            Handler = (_, _) => throw new InvalidOperationException("provider detail")
        };
        await using var fixture = await RerankingFixture.CreateAsync(reranker: reranker);
        var first = Chunk("first");
        var second = Chunk("second");
        fixture.VectorResults = [first, second];

        var result = await fixture.Service.SearchAsync(
            fixture.OwnerId,
            fixture.ProjectId,
            "provider failure",
            1,
            CancellationToken.None);

        Assert.Equal(first.ChunkId, Assert.Single(result!.Chunks).ChunkId);
        Assert.False(result.RerankingApplied);
        Assert.True(result.RerankingFallback);
        Assert.Null(result.Chunks[0].RerankRank);
    }

    [Fact]
    public async Task ProviderTimeoutFallsBackToHybridOrder()
    {
        var reranker = new RecordingReranker
        {
            Handler = async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return Result(("C2", 4));
            }
        };
        await using var fixture = await RerankingFixture.CreateAsync(
            reranker,
            new RetrievalRerankingOptions
            {
                Enabled = true,
                CandidateCount = 18,
                MaxCandidateCount = 30,
                MaxInputTokens = 12_000,
                MaxCandidateTokens = 700,
                TimeoutSeconds = 1
            });
        var first = Chunk("first");
        fixture.VectorResults = [first, Chunk("second")];

        var result = await fixture.Service.SearchAsync(
            fixture.OwnerId,
            fixture.ProjectId,
            "timeout",
            1,
            CancellationToken.None);

        Assert.Equal(first.ChunkId, Assert.Single(result!.Chunks).ChunkId);
        Assert.True(result.RerankingFallback);
        Assert.False(result.RerankingApplied);
    }

    [Fact]
    public async Task MissingRankingIsMalformedAndFallsBack()
    {
        var reranker = new RecordingReranker
        {
            Handler = (_, _) => Task.FromResult(new RetrievalRerankerResult(null))
        };
        await using var fixture = await RerankingFixture.CreateAsync(reranker: reranker);
        var first = Chunk("first");
        fixture.VectorResults = [first, Chunk("second")];

        var result = await fixture.Service.SearchAsync(
            fixture.OwnerId,
            fixture.ProjectId,
            "malformed output",
            1,
            CancellationToken.None);

        Assert.Equal(first.ChunkId, Assert.Single(result!.Chunks).ChunkId);
        Assert.True(result.RerankingFallback);
    }

    [Fact]
    public async Task RankingLongerThanSuppliedCandidateSetFallsBack()
    {
        var reranker = new RecordingReranker
        {
            Handler = (_, _) => Task.FromResult(Result(
                ("C2", 4),
                ("C1", 3),
                ("C2", 2)))
        };
        await using var fixture = await RerankingFixture.CreateAsync(reranker: reranker);
        var first = Chunk("first");
        fixture.VectorResults = [first, Chunk("second")];

        var result = await fixture.Service.SearchAsync(
            fixture.OwnerId,
            fixture.ProjectId,
            "overlong output",
            1,
            CancellationToken.None);

        Assert.Equal(first.ChunkId, Assert.Single(result!.Chunks).ChunkId);
        Assert.True(result.RerankingFallback);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public async Task OutOfRangeRelevanceMakesResultUntrusted(int relevance)
    {
        var reranker = new RecordingReranker
        {
            Handler = (_, _) => Task.FromResult(Result(("C2", relevance)))
        };
        await using var fixture = await RerankingFixture.CreateAsync(reranker: reranker);
        var first = Chunk("first");
        fixture.VectorResults = [first, Chunk("second")];

        var result = await fixture.Service.SearchAsync(
            fixture.OwnerId,
            fixture.ProjectId,
            "invalid relevance",
            1,
            CancellationToken.None);

        Assert.Equal(first.ChunkId, Assert.Single(result!.Chunks).ChunkId);
        Assert.True(result.RerankingFallback);
    }

    [Fact]
    public async Task UnknownIdsAreDiscardedDuplicatesKeepFirstAndOmissionsAppendInHybridOrder()
    {
        var reranker = new RecordingReranker
        {
            Handler = (_, _) => Task.FromResult(Result(
                ("C99", 4),
                ("C3", 4),
                ("C3", 0),
                ("C1", 3)))
        };
        await using var fixture = await RerankingFixture.CreateAsync(reranker: reranker);
        var first = Chunk("first");
        var omitted = Chunk("omitted second");
        var promoted = Chunk("promoted third");
        var fourth = Chunk("fourth");
        fixture.VectorResults = [first, omitted, promoted, fourth];

        var result = await fixture.Service.SearchAsync(
            fixture.OwnerId,
            fixture.ProjectId,
            "validate ids",
            3,
            CancellationToken.None);

        Assert.True(result!.RerankingApplied);
        Assert.Equal(
            [promoted.ChunkId, first.ChunkId, omitted.ChunkId],
            result.Chunks.Select(candidate => candidate.ChunkId));
        Assert.Equal([4, 3, null], result.Chunks.Select(candidate => candidate.RerankRelevance));
        Assert.Equal([1, 2, 3], result.Chunks.Select(candidate => candidate.RerankRank));
    }

    [Fact]
    public async Task RerankingCandidateCountHasHardCapAndFinalResultsRemainUnique()
    {
        var reranker = new RecordingReranker();
        await using var fixture = await RerankingFixture.CreateAsync(
            reranker,
            new RetrievalRerankingOptions
            {
                Enabled = true,
                CandidateCount = 30,
                MaxCandidateCount = 30,
                MaxInputTokens = 20_000,
                MaxCandidateTokens = 100,
                TimeoutSeconds = 30
            });
        var candidates = Enumerable.Range(0, 35)
            .Select(index => Chunk($"candidate {index}"))
            .ToArray();
        fixture.VectorResults = candidates;
        fixture.LexicalResults = [candidates[0], candidates[1], candidates[0]];

        var result = await fixture.Service.SearchAsync(
            fixture.OwnerId,
            fixture.ProjectId,
            "bounded batch",
            20,
            CancellationToken.None);

        Assert.Equal(30, Assert.Single(reranker.Calls).Candidates.Count);
        Assert.Equal(20, result!.Chunks.Count);
        Assert.Equal(
            result.Chunks.Count,
            result.Chunks.Select(candidate => candidate.ChunkId).Distinct().Count());
    }

    [Theory]
    [InlineData(
        "semantic disambiguation",
        "agreement customer termination notice formatting only",
        "early termination causes a fixed penalty",
        false)]
    [InlineData(
        "entity collision",
        "Vodafone invoice payment total",
        "Vodafone contract termination clause",
        false)]
    [InlineData(
        "negation and specificity",
        "fees do not apply to excluded service X",
        "fees apply to the case asked about",
        false)]
    [InlineData(
        "exact identifier",
        "CN-2026-00491 termination clause",
        "unrelated contract",
        true)]
    [InlineData(
        "semantic paraphrase",
        "tax residents abroad must provide a certificate",
        "generic tax information",
        true)]
    public async Task EvaluationComparesHybridAndFinalRankForKnownEvidence(
        string question,
        string firstContent,
        string secondContent,
        bool firstIsRelevant)
    {
        var relevantCandidateId = firstIsRelevant ? "C1" : "C2";
        var reranker = new RecordingReranker
        {
            Handler = (_, _) => Task.FromResult(Result(
                (relevantCandidateId, 4),
                (firstIsRelevant ? "C2" : "C1", 1)))
        };
        await using var fixture = await RerankingFixture.CreateAsync(reranker: reranker);
        var first = Chunk(firstContent);
        var second = Chunk(secondContent);
        var relevant = firstIsRelevant ? first : second;
        fixture.VectorResults = [first, second];

        var result = await fixture.Service.SearchAsync(
            fixture.OwnerId,
            fixture.ProjectId,
            question,
            1,
            CancellationToken.None);

        var selected = Assert.Single(result!.Chunks);
        Assert.Equal(relevant.ChunkId, selected.ChunkId);
        Assert.Equal(firstIsRelevant ? 1 : 2, selected.HybridRank);
        Assert.Equal(1, selected.RerankRank);
        Assert.Equal(4, selected.RerankRelevance);
    }

    [Fact]
    public async Task SearchAndAskUseTheSameRerankingOrchestration()
    {
        var reranker = new RecordingReranker
        {
            Handler = (_, _) => Task.FromResult(Result(("C2", 4), ("C1", 1)))
        };
        await using var fixture = await RerankingFixture.CreateAsync(reranker: reranker);
        var hybridFirst = Chunk("superficial match");
        var answerEvidence = Chunk("specific answer evidence");
        fixture.VectorResults = [hybridFirst, answerEvidence];
        var searchController = CreateSearchController(fixture.Service, fixture.OwnerId);
        var answerService = new RecordingAnswerService();
        var answerOptions = CreateAnswerOptions(topK: 1);
        var answeringService = new ProjectQuestionAnsweringService(
            fixture.Service,
            new RagContextBuilder(
                new Cl100kDocumentTokenizer(),
                Options.Create(answerOptions)),
            answerService,
            Options.Create(answerOptions),
            NullLogger<ProjectQuestionAnsweringService>.Instance);

        var searchAction = await searchController.Search(
            fixture.ProjectId,
            new SemanticSearchRequest { Query = "specific question", TopK = 1 },
            CancellationToken.None);
        var answer = await answeringService.AnswerAsync(
            fixture.OwnerId,
            fixture.ProjectId,
            "specific question",
            CancellationToken.None);

        var searchResponse = Assert.IsType<SemanticSearchResponse>(
            Assert.IsType<OkObjectResult>(searchAction.Result).Value);
        Assert.True(searchResponse.RerankingApplied);
        Assert.Equal(answerEvidence.ChunkId, Assert.Single(searchResponse.Results).ChunkId);
        Assert.Equal(answerEvidence.ChunkId, Assert.Single(answer!.Sources).ChunkId);
        Assert.Equal(2, reranker.Calls.Count);
        Assert.Single(answerService.Calls);
    }

    [Fact]
    public async Task RerankerFailureStillAllowsGroundedAskToContinueWithM13Evidence()
    {
        var reranker = new RecordingReranker
        {
            Handler = (_, _) => throw new RetrievalRerankingException("unavailable")
        };
        await using var fixture = await RerankingFixture.CreateAsync(reranker: reranker);
        var hybridFirst = Chunk("authoritative fallback evidence");
        fixture.VectorResults = [hybridFirst, Chunk("other evidence")];
        var answerService = new RecordingAnswerService();
        var options = CreateAnswerOptions(topK: 1);
        var service = new ProjectQuestionAnsweringService(
            fixture.Service,
            new RagContextBuilder(
                new Cl100kDocumentTokenizer(),
                Options.Create(options)),
            answerService,
            Options.Create(options),
            NullLogger<ProjectQuestionAnsweringService>.Instance);

        var result = await service.AnswerAsync(
            fixture.OwnerId,
            fixture.ProjectId,
            "fallback question",
            CancellationToken.None);

        Assert.Equal("Grounded fallback [S1]", result!.Answer);
        Assert.Equal(hybridFirst.ChunkId, Assert.Single(result.Sources).ChunkId);
        Assert.Single(reranker.Calls);
        Assert.Single(answerService.Calls);
        Assert.Single(fixture.EmbeddingCalls);
    }

    private static RetrievedDocumentChunk Chunk(string content) =>
        new(
            Guid.NewGuid(),
            "document.pdf",
            Guid.NewGuid(),
            0,
            content,
            1,
            1,
            "Heading",
            0.1);

    private static RetrievalRerankerResult Result(
        params (string CandidateId, int Relevance)[] ranks) =>
        new(ranks
            .Select(rank => new RetrievalRerankerRank(
                rank.CandidateId,
                rank.Relevance))
            .ToArray());

    private static OpenAIAnswerOptions CreateAnswerOptions(int topK) =>
        new()
        {
            AnswerModel = RagArchitecture.DefaultAnswerModel,
            AnswerRetrievalTopK = topK,
            MaxContextTokens = RagArchitecture.DefaultContextTokens,
            MaxAnswerTokens = RagArchitecture.DefaultAnswerTokens,
            SourceExcerptCharacters = RagArchitecture.DefaultSourceExcerptCharacters
        };

    private static ProjectIntelligenceController CreateSearchController(
        ISemanticRetrievalService retrievalService,
        Guid ownerId) =>
        new(retrievalService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, ownerId.ToString())],
                        "Test"))
                }
            }
        };

    private sealed class RecordingReranker : IRetrievalReranker
    {
        public List<RetrievalRerankingRequest> Calls { get; } = [];

        public Func<RetrievalRerankingRequest, CancellationToken,
            Task<RetrievalRerankerResult>> Handler { get; init; } =
            (request, _) => Task.FromResult(new RetrievalRerankerResult(
                request.Candidates
                    .Select(candidate => new RetrievalRerankerRank(
                        candidate.CandidateId,
                        2))
                    .ToArray()));

        public Task<RetrievalRerankerResult> RerankAsync(
            RetrievalRerankingRequest request,
            CancellationToken cancellationToken)
        {
            Calls.Add(request);
            return Handler(request, cancellationToken);
        }
    }

    private sealed class RecordingAnswerService : IGroundedAnswerService
    {
        public List<RagContext> Calls { get; } = [];

        public Task<GroundedModelAnswer> GenerateAnswerAsync(
            string question,
            RagContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(context);
            return Task.FromResult(new GroundedModelAnswer(
                "Grounded fallback [S1]",
                ["S1"]));
        }
    }

    private sealed class RerankingFixture : IAsyncDisposable
    {
        private readonly ApplicationDbContext _context;

        private RerankingFixture(
            ApplicationDbContext context,
            Guid ownerId,
            Guid projectId,
            RecordingVectorSearch vectorSearch,
            RecordingLexicalSearch lexicalSearch,
            SemanticRetrievalService service,
            List<IReadOnlyList<string>> embeddingCalls)
        {
            _context = context;
            OwnerId = ownerId;
            ProjectId = projectId;
            VectorSearch = vectorSearch;
            LexicalSearch = lexicalSearch;
            Service = service;
            EmbeddingCalls = embeddingCalls;
        }

        public Guid OwnerId { get; }

        public Guid ProjectId { get; }

        public RecordingVectorSearch VectorSearch { get; }

        public RecordingLexicalSearch LexicalSearch { get; }

        public SemanticRetrievalService Service { get; }

        public List<IReadOnlyList<string>> EmbeddingCalls { get; }

        public IReadOnlyList<RetrievedDocumentChunk> VectorResults
        {
            set => VectorSearch.Results = value;
        }

        public IReadOnlyList<RetrievedDocumentChunk> LexicalResults
        {
            set => LexicalSearch.Results = value;
        }

        public static async Task<RerankingFixture> CreateAsync(
            RecordingReranker? reranker = null,
            RetrievalRerankingOptions? rerankingOptions = null)
        {
            var context = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase($"reranking-{Guid.NewGuid():N}")
                    .Options);
            var ownerId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            context.Projects.Add(new Project
            {
                Id = projectId,
                OwnerId = ownerId,
                Name = "Reranking project",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await context.SaveChangesAsync();

            var embeddingCalls = new List<IReadOnlyList<string>>();
            var embeddingService = new RecordingEmbeddingService(embeddingCalls);
            var vectorSearch = new RecordingVectorSearch();
            var lexicalSearch = new RecordingLexicalSearch();
            var metadataSearch = new EmptyMetadataSearch();
            var hybridOptions = Options.Create(new HybridRetrievalOptions());
            var configuredRerankingOptions = Options.Create(
                rerankingOptions ?? new RetrievalRerankingOptions());
            var service = new SemanticRetrievalService(
                context,
                embeddingService,
                vectorSearch,
                lexicalSearch,
                metadataSearch,
                new DeterministicRetrievalQueryAnalyzer(),
                new ReciprocalRankFusion(hybridOptions),
                new RetrievalRerankingInputBuilder(
                    new Cl100kDocumentTokenizer(),
                    configuredRerankingOptions),
                reranker ?? new RecordingReranker(),
                Options.Create(new OpenAIEmbeddingOptions
                {
                    EmbeddingModel = EmbeddingArchitecture.DefaultModel,
                    EmbeddingDimensions = EmbeddingArchitecture.Dimensions,
                    BatchSize = 32
                }),
                hybridOptions,
                configuredRerankingOptions,
                NullLogger<SemanticRetrievalService>.Instance);

            return new RerankingFixture(
                context,
                ownerId,
                projectId,
                vectorSearch,
                lexicalSearch,
                service,
                embeddingCalls);
        }

        public ValueTask DisposeAsync() => _context.DisposeAsync();
    }

    private sealed class RecordingEmbeddingService(
        List<IReadOnlyList<string>> calls) : ITextEmbeddingService
    {
        public Task<TextEmbeddingResult> GenerateEmbeddingsAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add(texts.ToArray());
            var vector = new float[EmbeddingArchitecture.Dimensions];
            vector[0] = 1;
            return Task.FromResult(new TextEmbeddingResult(
                EmbeddingArchitecture.DefaultModel,
                EmbeddingArchitecture.Dimensions,
                [vector]));
        }
    }

    private sealed class RecordingVectorSearch : ISemanticChunkSearch
    {
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
            return Task.FromResult(Results);
        }
    }

    private sealed class RecordingLexicalSearch : ILexicalChunkSearch
    {
        public IReadOnlyList<RetrievedDocumentChunk> Results { get; set; } = [];

        public Task<IReadOnlyList<RetrievedDocumentChunk>> SearchAsync(
            Guid ownerId,
            Guid projectId,
            RetrievalQuery query,
            int candidateCount,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Results);
        }
    }

    private sealed class EmptyMetadataSearch : IMetadataDocumentSearch
    {
        public Task<IReadOnlyList<MetadataDocumentMatch>> SearchAsync(
            Guid ownerId,
            Guid projectId,
            RetrievalQuery query,
            int candidateCount,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<MetadataDocumentMatch>>([]);
        }
    }
}
