using AI.DocumentAssistant.Server.Retrieval;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Tests;

public sealed class HybridRetrievalFusionTests
{
    [Fact]
    public void VectorOnlyCandidateRetainsVectorDiagnostics()
    {
        var candidate = CreateChunk(distance: 0.12);

        var result = CreateFusion().Fuse([candidate], [], [], 8);

        var chunk = Assert.Single(result);
        Assert.Equal(1, chunk.VectorRank);
        Assert.Null(chunk.LexicalRank);
        Assert.Null(chunk.MetadataDocumentRank);
        Assert.Equal(0.12, chunk.CosineDistance);
        Assert.Equal(1.0 / 61.0, chunk.FusedScore!.Value, 12);
    }

    [Fact]
    public void LexicalOnlyCandidateRemainsEligibleWithoutAnEmbeddingResult()
    {
        var candidate = CreateChunk(lexicalScore: 0.81);

        var result = CreateFusion().Fuse([], [candidate], [], 8);

        var chunk = Assert.Single(result);
        Assert.Null(chunk.VectorRank);
        Assert.Equal(1, chunk.LexicalRank);
        Assert.Null(chunk.CosineDistance);
        Assert.Equal(0.81, chunk.LexicalRankScore);
        Assert.Equal(1.0 / 61.0, chunk.FusedScore!.Value, 12);
    }

    [Fact]
    public void CandidateInBothChannelsIsDeduplicatedAndReceivesBothContributions()
    {
        var chunkId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var vector = CreateChunk(chunkId, documentId, distance: 0.08);
        var lexical = CreateChunk(
            chunkId,
            documentId,
            lexicalScore: 0.72);

        var result = CreateFusion().Fuse([vector], [lexical], [], 8);

        var chunk = Assert.Single(result);
        Assert.Equal(chunkId, chunk.ChunkId);
        Assert.Equal(1, chunk.VectorRank);
        Assert.Equal(1, chunk.LexicalRank);
        Assert.Equal(2.0 / 61.0, chunk.FusedScore!.Value, 12);
    }

    [Fact]
    public void MetadataDocumentRankBoostsOnlyExistingChunkCandidates()
    {
        var preferredDocumentId = Guid.NewGuid();
        var other = CreateChunk(documentId: Guid.NewGuid(), distance: 0.05);
        var preferred = CreateChunk(documentId: preferredDocumentId, distance: 0.08);
        var metadataOnlyDocumentId = Guid.NewGuid();
        var metadata = new[]
        {
            new MetadataDocumentMatch(
                preferredDocumentId,
                1,
                8,
                false,
                [new MatchedRetrievalMetadata("Organization", "Vodafone", false)]),
            new MetadataDocumentMatch(metadataOnlyDocumentId, 2, 7, false, [])
        };

        var result = CreateFusion().Fuse([other, preferred], [], metadata, 8);

        Assert.Equal(preferred.ChunkId, result[0].ChunkId);
        Assert.Equal(1, result[0].MetadataDocumentRank);
        Assert.Equal("Vodafone", Assert.Single(result[0].MatchedMetadata!).Value);
        Assert.DoesNotContain(result, chunk => chunk.DocumentId == metadataOnlyDocumentId);
    }

    [Fact]
    public void DuplicateChunkIdsWithinAndAcrossChannelsAppearOnlyOnce()
    {
        var chunk = CreateChunk(distance: 0.1);
        var lexical = chunk with { CosineDistance = null, LexicalRankScore = 0.9 };

        var result = CreateFusion().Fuse(
            [chunk, chunk],
            [lexical, lexical],
            [],
            8);

        Assert.Single(result);
        Assert.Equal(1, result[0].VectorRank);
        Assert.Equal(1, result[0].LexicalRank);
    }

    [Fact]
    public void RrfFormulaUsesCentralizedChannelWeightsAndExactIdentifierMultiplier()
    {
        var target = CreateChunk(documentId: Guid.NewGuid(), distance: 0.1);
        var vectorFirst = CreateChunk(documentId: Guid.NewGuid(), distance: 0.05);
        var lexicalFirst = CreateChunk(documentId: Guid.NewGuid(), lexicalScore: 0.9);
        var lexicalTarget = target with
        {
            CosineDistance = null,
            LexicalRankScore = 0.8
        };
        var options = new HybridRetrievalOptions
        {
            ReciprocalRankConstant = 10,
            VectorWeight = 2,
            LexicalWeight = 3,
            MetadataWeight = 0.4,
            ExactIdentifierMultiplier = 1.5
        };
        var metadata = new MetadataDocumentMatch(
            target.DocumentId,
            3,
            12,
            true,
            [new MatchedRetrievalMetadata("Identifier", "CN-2026-00491", true)]);

        var result = CreateFusion(options).Fuse(
            [vectorFirst, target],
            [lexicalFirst, lexicalTarget],
            [metadata],
            3);

        var fusedTarget = Assert.Single(result, item => item.ChunkId == target.ChunkId);
        var expected = (2.0 / 12.0) + (3.0 / 12.0) + (0.6 / 13.0);
        Assert.Equal(expected, fusedTarget.FusedScore!.Value, 12);
        Assert.True(Assert.Single(fusedTarget.MatchedMetadata!).IsExact);
    }

    [Fact]
    public void EqualScoresUseStableDocumentChunkAndChunkIdTieBreakers()
    {
        var firstDocumentId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondDocumentId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var first = CreateChunk(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            firstDocumentId,
            distance: 0.1);
        var second = CreateChunk(
            Guid.Parse("10000000-0000-0000-0000-000000000002"),
            secondDocumentId,
            distance: 0.1);

        var result = CreateFusion().Fuse(
            [second, first],
            [first with { CosineDistance = null }, second with { CosineDistance = null }],
            [],
            2);

        Assert.Equal([first.ChunkId, second.ChunkId], result.Select(item => item.ChunkId));
    }

    [Fact]
    public void RequestedTopKBoundsFinalUniqueResults()
    {
        var candidates = Enumerable.Range(0, 6)
            .Select(index => CreateChunk(chunkIndex: index, distance: index / 10.0))
            .ToArray();

        var result = CreateFusion().Fuse(candidates, [], [], 2);

        Assert.Equal(2, result.Count);
        Assert.Equal([1, 2], result.Select(item => item.VectorRank));
    }

    [Fact]
    public void OrganizationMetadataFavorsTheMatchingDocumentWhenEvidenceRanksAreClose()
    {
        var vodafone = CreateChunk(documentId: Guid.NewGuid(), distance: 0.11);
        var boilerplate = CreateChunk(documentId: Guid.NewGuid(), distance: 0.10);
        var metadata = new MetadataDocumentMatch(
            vodafone.DocumentId,
            1,
            8,
            false,
            [
                new MatchedRetrievalMetadata("Organization", "Vodafone", false),
                new MatchedRetrievalMetadata("DocumentType", "Contract", false)
            ]);

        var result = CreateFusion().Fuse(
            [boilerplate, vodafone],
            [boilerplate with { CosineDistance = null }, vodafone with { CosineDistance = null }],
            [metadata],
            2);

        Assert.Equal(vodafone.ChunkId, result[0].ChunkId);
    }

    [Fact]
    public void SoftMetadataBoostDoesNotOvertakeClearlyStrongerVectorAndLexicalEvidence()
    {
        var strongest = CreateChunk(documentId: Guid.NewGuid(), distance: 0.01);
        var misleading = CreateChunk(documentId: Guid.NewGuid(), distance: 0.5);
        var vector = BuildTwentyRankedCandidates(strongest, misleading, lexical: false);
        var lexical = BuildTwentyRankedCandidates(strongest, misleading, lexical: true);
        var metadata = new MetadataDocumentMatch(
            misleading.DocumentId,
            1,
            4,
            false,
            [new MatchedRetrievalMetadata("Topic", "Vodafone", false)]);

        var result = CreateFusion().Fuse(vector, lexical, [metadata], 20);

        Assert.True(
            result.FindIndex(item => item.ChunkId == strongest.ChunkId) <
            result.FindIndex(item => item.ChunkId == misleading.ChunkId));
    }

    [Fact]
    public void LegacyDocumentWithoutMetadataStillRanksThroughVectorAndLexicalChannels()
    {
        var semantic = CreateChunk(distance: 0.05);
        var exact = CreateChunk(lexicalScore: 0.9);

        var result = CreateFusion().Fuse([semantic], [exact], [], 8);

        Assert.Equal(2, result.Count);
        Assert.All(result, item => Assert.Null(item.MetadataDocumentRank));
    }

    private static ReciprocalRankFusion CreateFusion(
        HybridRetrievalOptions? options = null) =>
        new(Options.Create(options ?? new HybridRetrievalOptions()));

    private static RetrievedDocumentChunk CreateChunk(
        Guid? chunkId = null,
        Guid? documentId = null,
        int chunkIndex = 0,
        double? distance = null,
        double? lexicalScore = null) =>
        new(
            documentId ?? Guid.NewGuid(),
            "document.pdf",
            chunkId ?? Guid.NewGuid(),
            chunkIndex,
            "Authoritative chunk content.",
            1,
            1,
            null,
            distance,
            LexicalRankScore: lexicalScore);

    private static RetrievedDocumentChunk[] BuildTwentyRankedCandidates(
        RetrievedDocumentChunk strongest,
        RetrievedDocumentChunk weakest,
        bool lexical)
    {
        var candidates = new List<RetrievedDocumentChunk> { strongest };
        candidates.AddRange(Enumerable.Range(1, 18).Select(index =>
            CreateChunk(chunkIndex: index, distance: 0.1 + index / 100.0)));
        candidates.Add(weakest);

        return lexical
            ? candidates.Select(item => item with
                {
                    CosineDistance = null,
                    LexicalRankScore = 1
                }).ToArray()
            : candidates.ToArray();
    }
}

internal static class RetrievedChunkListExtensions
{
    public static int FindIndex(
        this IReadOnlyList<RetrievedDocumentChunk> chunks,
        Func<RetrievedDocumentChunk, bool> predicate)
    {
        for (var index = 0; index < chunks.Count; index++)
        {
            if (predicate(chunks[index]))
            {
                return index;
            }
        }

        return -1;
    }
}
