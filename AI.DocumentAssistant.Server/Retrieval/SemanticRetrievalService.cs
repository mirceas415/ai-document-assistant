using System.Diagnostics;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Embeddings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector;

namespace AI.DocumentAssistant.Server.Retrieval;

public sealed class SemanticRetrievalService : ISemanticRetrievalService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ITextEmbeddingService _embeddingService;
    private readonly ISemanticChunkSearch _chunkSearch;
    private readonly ILexicalChunkSearch _lexicalSearch;
    private readonly IMetadataDocumentSearch _metadataSearch;
    private readonly IRetrievalQueryAnalyzer _queryAnalyzer;
    private readonly IHybridRetrievalFusion _fusion;
    private readonly IRetrievalRerankingInputBuilder _rerankingInputBuilder;
    private readonly IRetrievalReranker _reranker;
    private readonly OpenAIEmbeddingOptions _embeddingOptions;
    private readonly HybridRetrievalOptions _hybridOptions;
    private readonly RetrievalRerankingOptions _rerankingOptions;
    private readonly ILogger<SemanticRetrievalService> _logger;

    public SemanticRetrievalService(
        ApplicationDbContext dbContext,
        ITextEmbeddingService embeddingService,
        ISemanticChunkSearch chunkSearch,
        ILexicalChunkSearch lexicalSearch,
        IMetadataDocumentSearch metadataSearch,
        IRetrievalQueryAnalyzer queryAnalyzer,
        IHybridRetrievalFusion fusion,
        IRetrievalRerankingInputBuilder rerankingInputBuilder,
        IRetrievalReranker reranker,
        IOptions<OpenAIEmbeddingOptions> embeddingOptions,
        IOptions<HybridRetrievalOptions> hybridOptions,
        IOptions<RetrievalRerankingOptions> rerankingOptions,
        ILogger<SemanticRetrievalService> logger)
    {
        _dbContext = dbContext;
        _embeddingService = embeddingService;
        _chunkSearch = chunkSearch;
        _lexicalSearch = lexicalSearch;
        _metadataSearch = metadataSearch;
        _queryAnalyzer = queryAnalyzer;
        _fusion = fusion;
        _rerankingInputBuilder = rerankingInputBuilder;
        _reranker = reranker;
        _embeddingOptions = embeddingOptions.Value;
        _hybridOptions = hybridOptions.Value;
        _rerankingOptions = rerankingOptions.Value;
        _logger = logger;
    }

    public async Task<SemanticRetrievalResult?> SearchAsync(
        Guid ownerId,
        Guid projectId,
        string query,
        int topK,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(topK, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            topK,
            SemanticRetrievalLimits.MaximumTopK);

        var projectExists = await _dbContext.Projects
            .AsNoTracking()
            .AnyAsync(
                project => project.Id == projectId && project.OwnerId == ownerId,
                cancellationToken);
        if (!projectExists)
        {
            return null;
        }

        var normalizedQuery = query.Trim();
        var preparedQuery = _queryAnalyzer.Analyze(normalizedQuery);
        var vectorCandidateCount = Math.Clamp(
            Math.Max(_hybridOptions.VectorCandidateCount, topK),
            1,
            SemanticRetrievalLimits.MaximumCandidateCount);
        var lexicalCandidateCount = Math.Clamp(
            Math.Max(_hybridOptions.LexicalCandidateCount, topK),
            1,
            SemanticRetrievalLimits.MaximumCandidateCount);
        var metadataCandidateCount = Math.Clamp(
            _hybridOptions.MetadataDocumentCandidateCount,
            1,
            SemanticRetrievalLimits.MaximumMetadataDocumentCandidateCount);
        var fusionCandidateCount = GetFusionCandidateCount(topK);
        var stopwatch = Stopwatch.StartNew();
        var embeddingResult = await _embeddingService.GenerateEmbeddingsAsync(
            [normalizedQuery],
            cancellationToken);
        EmbeddingResultValidator.Validate(
            embeddingResult,
            expectedCount: 1,
            _embeddingOptions.EmbeddingModel,
            _embeddingOptions.EmbeddingDimensions);

        var vectorCandidates = await _chunkSearch.SearchAsync(
            ownerId,
            projectId,
            new Vector(embeddingResult.Embeddings[0]),
            _embeddingOptions.EmbeddingModel,
            _embeddingOptions.EmbeddingDimensions,
            vectorCandidateCount,
            cancellationToken);
        var lexicalCandidates = await _lexicalSearch.SearchAsync(
            ownerId,
            projectId,
            preparedQuery,
            lexicalCandidateCount,
            cancellationToken);
        var metadataDocuments = await _metadataSearch.SearchAsync(
            ownerId,
            projectId,
            preparedQuery,
            metadataCandidateCount,
            cancellationToken);
        var hybridCandidates = _fusion.Fuse(
                vectorCandidates,
                lexicalCandidates,
                metadataDocuments,
                fusionCandidateCount)
            .Select((chunk, index) => chunk with
            {
                HybridRank = index + 1,
                RerankRank = null,
                RerankRelevance = null
            })
            .ToArray();
        var reranking = await ApplyRerankingAsync(
            normalizedQuery,
            hybridCandidates,
            topK,
            cancellationToken);
        var chunks = reranking.Chunks;

        stopwatch.Stop();
        _logger.LogInformation(
            "Hybrid retrieval and optional reranking completed for project {ProjectId} with {VectorCandidateCount} vector candidates, {LexicalCandidateCount} lexical candidates, {MetadataDocumentCount} metadata documents, {HybridCandidateCount} fused candidates, and {ResultCount} final results (TopK {TopK}). Reranking applied: {RerankingApplied}; fallback: {RerankingFallback}; duration: {DurationMs} ms; embedding model: {EmbeddingModel}; dimensions: {EmbeddingDimensions}.",
            projectId,
            vectorCandidates.Count,
            lexicalCandidates.Count,
            metadataDocuments.Count,
            hybridCandidates.Length,
            chunks.Count,
            topK,
            reranking.Applied,
            reranking.Fallback,
            stopwatch.ElapsedMilliseconds,
            _embeddingOptions.EmbeddingModel,
            _embeddingOptions.EmbeddingDimensions);

        return new SemanticRetrievalResult(
            topK,
            chunks,
            reranking.Applied,
            reranking.Fallback);
    }

    private int GetFusionCandidateCount(int topK)
    {
        if (!_rerankingOptions.Enabled)
        {
            return topK;
        }

        var configuredCandidateCount = Math.Clamp(
            Math.Min(
                _rerankingOptions.CandidateCount,
                _rerankingOptions.MaxCandidateCount),
            2,
            RetrievalRerankingLimits.MaximumCandidateCount);
        return Math.Max(topK, configuredCandidateCount);
    }

    private async Task<RerankingOutcome> ApplyRerankingAsync(
        string question,
        IReadOnlyList<RetrievedDocumentChunk> hybridCandidates,
        int topK,
        CancellationToken cancellationToken)
    {
        var hybridTopK = hybridCandidates.Take(topK).ToArray();
        if (!_rerankingOptions.Enabled ||
            hybridCandidates.Count <= 1 ||
            hybridCandidates.Count <= topK)
        {
            return new RerankingOutcome(hybridTopK, false, false);
        }

        RetrievalRerankingRequest request;
        try
        {
            request = _rerankingInputBuilder.Build(question, hybridCandidates);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Retrieval reranking input preparation failed with exception type {ExceptionType}. Hybrid ordering was retained; question and candidate content were omitted.",
                exception.GetType().FullName);
            return new RerankingOutcome(hybridTopK, false, true);
        }

        if (request.Candidates.Count <= topK)
        {
            return new RerankingOutcome(hybridTopK, false, false);
        }

        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(
                _rerankingOptions.TimeoutSeconds,
                1,
                RetrievalRerankingLimits.MaximumTimeoutSeconds)));

            var providerResult = await _reranker.RerankAsync(
                request,
                timeoutSource.Token);
            if (!TryApplyProviderRanking(
                    hybridCandidates,
                    request,
                    providerResult,
                    out var rerankedCandidates))
            {
                _logger.LogWarning(
                    "Retrieval reranking returned an invalid bounded ranking for {CandidateCount} candidates. Hybrid ordering was retained; provider output was omitted.",
                    request.Candidates.Count);
                return new RerankingOutcome(hybridTopK, false, true);
            }

            _logger.LogInformation(
                "Retrieval reranking completed for {CandidateCount} candidates with approximately {InputTokenCount} input tokens. Reranking applied: true; fallback: false.",
                request.Candidates.Count,
                request.ApproximateInputTokenCount);
            return new RerankingOutcome(
                rerankedCandidates.Take(topK).ToArray(),
                true,
                false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Retrieval reranking exceeded its {TimeoutSeconds}-second timeout. Hybrid ordering was retained; question and candidate content were omitted.",
                _rerankingOptions.TimeoutSeconds);
            return new RerankingOutcome(hybridTopK, false, true);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Retrieval reranking failed with exception type {ExceptionType}. Hybrid ordering was retained; question, candidate content, and provider details were omitted.",
                exception.GetType().FullName);
            return new RerankingOutcome(hybridTopK, false, true);
        }
    }

    private static bool TryApplyProviderRanking(
        IReadOnlyList<RetrievedDocumentChunk> hybridCandidates,
        RetrievalRerankingRequest request,
        RetrievalRerankerResult? providerResult,
        out IReadOnlyList<RetrievedDocumentChunk> rerankedCandidates)
    {
        rerankedCandidates = [];
        if (providerResult?.Ranking is null ||
            providerResult.Ranking.Count == 0 ||
            providerResult.Ranking.Count > request.Candidates.Count)
        {
            return false;
        }

        var requestCandidatesById = request.Candidates.ToDictionary(
            candidate => candidate.CandidateId,
            StringComparer.Ordinal);
        var chunksById = hybridCandidates.ToDictionary(
            candidate => candidate.ChunkId);
        var seenCandidateIds = new HashSet<string>(StringComparer.Ordinal);
        var seenChunkIds = new HashSet<Guid>();
        var relevanceByChunkId = new Dictionary<Guid, int>();
        var ordered = new List<RetrievedDocumentChunk>(hybridCandidates.Count);

        foreach (var rank in providerResult.Ranking)
        {
            if (rank is null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(rank.CandidateId) ||
                !requestCandidatesById.TryGetValue(rank.CandidateId, out var candidate))
            {
                continue;
            }

            if (!seenCandidateIds.Add(rank.CandidateId))
            {
                continue;
            }

            if (rank.Relevance is null or
                < RetrievalRerankingLimits.MinimumRelevance or
                > RetrievalRerankingLimits.MaximumRelevance ||
                !chunksById.TryGetValue(candidate.ChunkId, out var chunk))
            {
                return false;
            }

            ordered.Add(chunk);
            seenChunkIds.Add(chunk.ChunkId);
            relevanceByChunkId.Add(chunk.ChunkId, rank.Relevance.Value);
        }

        if (ordered.Count == 0)
        {
            return false;
        }

        foreach (var candidate in hybridCandidates)
        {
            if (seenChunkIds.Add(candidate.ChunkId))
            {
                ordered.Add(candidate);
            }
        }

        rerankedCandidates = ordered
            .Select((candidate, index) => candidate with
            {
                RerankRank = index + 1,
                RerankRelevance = relevanceByChunkId.TryGetValue(
                    candidate.ChunkId,
                    out var relevance)
                    ? relevance
                    : null
            })
            .ToArray();
        return true;
    }

    private sealed record RerankingOutcome(
        IReadOnlyList<RetrievedDocumentChunk> Chunks,
        bool Applied,
        bool Fallback);
}
