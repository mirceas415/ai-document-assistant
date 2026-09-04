using AI.DocumentAssistant.Server.Retrieval;
using Microsoft.Extensions.Options;

namespace AI.DocumentAssistant.Server.Tests;

public sealed class HybridRetrievalEvaluationTests
{
    [Fact]
    public void SyntheticParaphraseIdentifierOrganizationTypeAndGenericCasesRankExpectedChunkFirst()
    {
        var fusion = new ReciprocalRankFusion(
            Options.Create(new HybridRetrievalOptions()));
        var cases = CreateCases();

        foreach (var evaluationCase in cases)
        {
            var results = fusion.Fuse(
                evaluationCase.VectorCandidates,
                evaluationCase.LexicalCandidates,
                evaluationCase.MetadataDocuments,
                2);

            Assert.Equal(
                evaluationCase.ExpectedChunkId,
                Assert.IsType<RetrievedDocumentChunk>(results[0]).ChunkId);
        }
    }

    private static IReadOnlyList<EvaluationCase> CreateCases()
    {
        var paraphrase = Pair("tax-residency.pdf");
        var identifier = Pair("contract-CN-2026-00491.pdf");
        var organization = Pair("vodafone-contract.pdf");
        var documentType = Pair("invoice-2026.pdf");
        var generic = Pair("course-policy.pdf");

        return
        [
            new EvaluationCase(
                "semantic paraphrase",
                paraphrase.Relevant.ChunkId,
                [paraphrase.Relevant, paraphrase.Distractor],
                [],
                []),
            new EvaluationCase(
                "exact identifier",
                identifier.Relevant.ChunkId,
                [identifier.Distractor, identifier.Relevant],
                [identifier.Relevant with { CosineDistance = null, LexicalRankScore = 1 }],
                [Metadata(
                    identifier.Relevant.DocumentId,
                    "Identifier",
                    "CN-2026-00491",
                    exactIdentifier: true)]),
            new EvaluationCase(
                "organization",
                organization.Relevant.ChunkId,
                [organization.Distractor, organization.Relevant],
                [
                    organization.Distractor with { CosineDistance = null, LexicalRankScore = 1 },
                    organization.Relevant with { CosineDistance = null, LexicalRankScore = 0.9 }
                ],
                [Metadata(organization.Relevant.DocumentId, "Organization", "Vodafone")]),
            new EvaluationCase(
                "document type",
                documentType.Relevant.ChunkId,
                [documentType.Distractor, documentType.Relevant],
                [
                    documentType.Distractor with { CosineDistance = null, LexicalRankScore = 1 },
                    documentType.Relevant with { CosineDistance = null, LexicalRankScore = 0.9 }
                ],
                [Metadata(documentType.Relevant.DocumentId, "DocumentType", "Invoice")]),
            new EvaluationCase(
                "generic question",
                generic.Relevant.ChunkId,
                [generic.Relevant, generic.Distractor],
                [generic.Relevant with { CosineDistance = null, LexicalRankScore = 1 }],
                [])
        ];
    }

    private static CandidatePair Pair(string relevantName)
    {
        var relevant = Chunk(relevantName);
        return new CandidatePair(relevant, Chunk("distractor.pdf"));
    }

    private static RetrievedDocumentChunk Chunk(string documentName) =>
        new(
            Guid.NewGuid(),
            documentName,
            Guid.NewGuid(),
            0,
            "Synthetic authoritative evidence.",
            1,
            1,
            null,
            0.1);

    private static MetadataDocumentMatch Metadata(
        Guid documentId,
        string field,
        string value,
        bool exactIdentifier = false) =>
        new(
            documentId,
            1,
            exactIdentifier ? 12 : 4,
            exactIdentifier,
            [new MatchedRetrievalMetadata(field, value, exactIdentifier)]);

    private sealed record CandidatePair(
        RetrievedDocumentChunk Relevant,
        RetrievedDocumentChunk Distractor);

    private sealed record EvaluationCase(
        string Name,
        Guid ExpectedChunkId,
        IReadOnlyList<RetrievedDocumentChunk> VectorCandidates,
        IReadOnlyList<RetrievedDocumentChunk> LexicalCandidates,
        IReadOnlyList<MetadataDocumentMatch> MetadataDocuments);
}
