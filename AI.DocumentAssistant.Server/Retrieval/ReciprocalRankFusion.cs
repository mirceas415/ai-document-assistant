using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Retrieval;

public sealed class ReciprocalRankFusion : IHybridRetrievalFusion
{
    private readonly HybridRetrievalOptions _options;

    public ReciprocalRankFusion(IOptions<HybridRetrievalOptions> options)
    {
        _options = options.Value;
    }

    public IReadOnlyList<RetrievedDocumentChunk> Fuse(
        IReadOnlyList<RetrievedDocumentChunk> vectorCandidates,
        IReadOnlyList<RetrievedDocumentChunk> lexicalCandidates,
        IReadOnlyList<MetadataDocumentMatch> metadataDocuments,
        int topK)
    {
        ArgumentNullException.ThrowIfNull(vectorCandidates);
        ArgumentNullException.ThrowIfNull(lexicalCandidates);
        ArgumentNullException.ThrowIfNull(metadataDocuments);
        ArgumentOutOfRangeException.ThrowIfLessThan(topK, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            topK,
            SemanticRetrievalLimits.MaximumTopK);

        var vectorsByChunk = RankUniqueChunks(vectorCandidates);
        var lexicalByChunk = RankUniqueChunks(lexicalCandidates);
        var metadataByDocument = metadataDocuments
            .OrderBy(value => value.Rank)
            .GroupBy(value => value.DocumentId)
            .ToDictionary(group => group.Key, group => group.First());
        var chunkIds = vectorsByChunk.Keys
            .Concat(lexicalByChunk.Keys)
            .Distinct()
            .ToArray();

        var fused = new List<FusedCandidate>(chunkIds.Length);
        foreach (var chunkId in chunkIds)
        {
            vectorsByChunk.TryGetValue(chunkId, out var vector);
            lexicalByChunk.TryGetValue(chunkId, out var lexical);
            var chunk = vector?.Chunk ?? lexical!.Chunk;
            metadataByDocument.TryGetValue(chunk.DocumentId, out var metadata);

            var score = Contribution(vector?.Rank, _options.VectorWeight)
                + Contribution(lexical?.Rank, _options.LexicalWeight);
            if (metadata is not null)
            {
                var metadataWeight = _options.MetadataWeight *
                    (metadata.HasExactIdentifierMatch
                        ? _options.ExactIdentifierMultiplier
                        : 1.0);
                score += Contribution(metadata.Rank, metadataWeight);
            }

            var bestIndividualRank = new int?[]
                {
                    vector?.Rank,
                    lexical?.Rank,
                    metadata?.Rank
                }
                .Where(value => value.HasValue)
                .Min(value => value!.Value);

            fused.Add(new FusedCandidate(
                chunk with
                {
                    CosineDistance = vector?.Chunk.CosineDistance,
                    VectorRank = vector?.Rank,
                    LexicalRank = lexical?.Rank,
                    MetadataDocumentRank = metadata?.Rank,
                    LexicalRankScore = lexical?.Chunk.LexicalRankScore,
                    FusedScore = score,
                    MatchedMetadata = metadata?.MatchedMetadata ?? []
                },
                score,
                bestIndividualRank));
        }

        return fused
            .OrderByDescending(value => value.Score)
            .ThenBy(value => value.BestIndividualRank)
            .ThenBy(value => value.Chunk.DocumentId)
            .ThenBy(value => value.Chunk.ChunkIndex)
            .ThenBy(value => value.Chunk.ChunkId)
            .Take(topK)
            .Select(value => value.Chunk)
            .ToArray();
    }

    private double Contribution(int? rank, double weight) =>
        rank.HasValue
            ? weight / (_options.ReciprocalRankConstant + rank.Value)
            : 0;

    private static Dictionary<Guid, RankedChunk> RankUniqueChunks(
        IReadOnlyList<RetrievedDocumentChunk> candidates)
    {
        var ranked = new Dictionary<Guid, RankedChunk>();
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            ranked.TryAdd(candidate.ChunkId, new RankedChunk(candidate, index + 1));
        }

        return ranked;
    }

    private sealed record RankedChunk(RetrievedDocumentChunk Chunk, int Rank);

    private sealed record FusedCandidate(
        RetrievedDocumentChunk Chunk,
        double Score,
        int BestIndividualRank);
}
