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
    private readonly OpenAIEmbeddingOptions _embeddingOptions;
    private readonly HybridRetrievalOptions _hybridOptions;
    private readonly ILogger<SemanticRetrievalService> _logger;

    public SemanticRetrievalService(
        ApplicationDbContext dbContext,
        ITextEmbeddingService embeddingService,
        ISemanticChunkSearch chunkSearch,
        ILexicalChunkSearch lexicalSearch,
        IMetadataDocumentSearch metadataSearch,
        IRetrievalQueryAnalyzer queryAnalyzer,
        IHybridRetrievalFusion fusion,
        IOptions<OpenAIEmbeddingOptions> embeddingOptions,
        IOptions<HybridRetrievalOptions> hybridOptions,
        ILogger<SemanticRetrievalService> logger)
    {
        _dbContext = dbContext;
        _embeddingService = embeddingService;
        _chunkSearch = chunkSearch;
        _lexicalSearch = lexicalSearch;
        _metadataSearch = metadataSearch;
        _queryAnalyzer = queryAnalyzer;
        _fusion = fusion;
        _embeddingOptions = embeddingOptions.Value;
        _hybridOptions = hybridOptions.Value;
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
        var chunks = _fusion.Fuse(
            vectorCandidates,
            lexicalCandidates,
            metadataDocuments,
            topK);

        stopwatch.Stop();
        _logger.LogInformation(
            "Hybrid retrieval completed for project {ProjectId} with {VectorCandidateCount} vector candidates, {LexicalCandidateCount} lexical candidates, {MetadataDocumentCount} metadata documents, and {ResultCount} final results (TopK {TopK}) in {DurationMs} ms using embedding model {EmbeddingModel} with {EmbeddingDimensions} dimensions.",
            projectId,
            vectorCandidates.Count,
            lexicalCandidates.Count,
            metadataDocuments.Count,
            chunks.Count,
            topK,
            stopwatch.ElapsedMilliseconds,
            _embeddingOptions.EmbeddingModel,
            _embeddingOptions.EmbeddingDimensions);

        return new SemanticRetrievalResult(topK, chunks);
    }
}
