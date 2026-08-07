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
    private readonly OpenAIEmbeddingOptions _embeddingOptions;
    private readonly ILogger<SemanticRetrievalService> _logger;

    public SemanticRetrievalService(
        ApplicationDbContext dbContext,
        ITextEmbeddingService embeddingService,
        ISemanticChunkSearch chunkSearch,
        IOptions<OpenAIEmbeddingOptions> embeddingOptions,
        ILogger<SemanticRetrievalService> logger)
    {
        _dbContext = dbContext;
        _embeddingService = embeddingService;
        _chunkSearch = chunkSearch;
        _embeddingOptions = embeddingOptions.Value;
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
        var stopwatch = Stopwatch.StartNew();
        var embeddingResult = await _embeddingService.GenerateEmbeddingsAsync(
            [normalizedQuery],
            cancellationToken);
        EmbeddingResultValidator.Validate(
            embeddingResult,
            expectedCount: 1,
            _embeddingOptions.EmbeddingModel,
            _embeddingOptions.EmbeddingDimensions);

        var chunks = await _chunkSearch.SearchAsync(
            ownerId,
            projectId,
            new Vector(embeddingResult.Embeddings[0]),
            _embeddingOptions.EmbeddingModel,
            _embeddingOptions.EmbeddingDimensions,
            topK,
            cancellationToken);

        stopwatch.Stop();
        _logger.LogInformation(
            "Semantic retrieval completed for project {ProjectId} with {ResultCount} results (TopK {TopK}) in {DurationMs} ms using embedding model {EmbeddingModel} with {EmbeddingDimensions} dimensions.",
            projectId,
            chunks.Count,
            topK,
            stopwatch.ElapsedMilliseconds,
            _embeddingOptions.EmbeddingModel,
            _embeddingOptions.EmbeddingDimensions);

        return new SemanticRetrievalResult(topK, chunks);
    }
}
