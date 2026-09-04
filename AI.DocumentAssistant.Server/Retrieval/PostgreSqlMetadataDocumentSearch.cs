using System.Data;
using System.Data.Common;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.Server.Retrieval;

public sealed class PostgreSqlMetadataDocumentSearch : IMetadataDocumentSearch
{
    public const string SearchSql = """
        WITH eligible_documents AS (
            SELECT
                d."Id" AS "DocumentId",
                d."OriginalFileName" AS "DocumentName",
                u."DocumentType",
                u."DocumentSubtype",
                u."DetectedTitle",
                u."Subject"
            FROM "Documents" AS d
            INNER JOIN "Projects" AS p ON p."Id" = d."ProjectId"
            INNER JOIN "DocumentUnderstandings" AS u ON u."DocumentId" = d."Id"
            WHERE p."Id" = @project_id
              AND p."OwnerId" = @owner_id
              AND d."ProjectId" = @project_id
              AND d."Status" = @ready_status
              AND u."Status" = @understanding_ready_status
        ),
        metadata_matches AS (
            SELECT
                m."Id",
                m."DocumentUnderstandingId" AS "DocumentId",
                m."Kind",
                m."Label",
                m."Value",
                m."Sequence",
                CASE
                    WHEN m."Kind" = 'Identifier'
                         AND lower(COALESCE(m."NormalizedValue", m."Value")) =
                             ANY(@identifier_values::text[])
                        THEN 12.0::double precision
                    WHEN m."Kind" = 'Date'
                         AND lower(COALESCE(m."NormalizedValue", m."Value")) =
                             ANY(@date_values::text[])
                        THEN 6.0::double precision
                    WHEN m."Kind" = 'MonetaryAmount'
                         AND upper(regexp_replace(m."Value", '[^[:alnum:]]', '', 'g')) =
                             ANY(@monetary_values::text[])
                        THEN 6.0::double precision
                    WHEN EXISTS (
                        SELECT 1
                        FROM unnest(@search_terms::text[]) AS query_term(value)
                        WHERE to_tsvector(
                                'simple',
                                concat_ws(' ', m."Label", m."Value", m."NormalizedValue"))
                              @@ plainto_tsquery('simple', query_term.value)
                    )
                        THEN CASE m."Kind"
                            WHEN 'Organization' THEN 4.0::double precision
                            WHEN 'Identifier' THEN 4.0::double precision
                            WHEN 'Date' THEN 3.0::double precision
                            WHEN 'MonetaryAmount' THEN 3.0::double precision
                            ELSE 2.0::double precision
                        END
                    ELSE 0.0::double precision
                END AS "MatchScore",
                m."Kind" = 'Identifier'
                    AND lower(COALESCE(m."NormalizedValue", m."Value")) =
                        ANY(@identifier_values::text[]) AS "ExactIdentifierMatch",
                (m."Kind" = 'Date'
                    AND lower(COALESCE(m."NormalizedValue", m."Value")) =
                        ANY(@date_values::text[]))
                    OR (m."Kind" = 'MonetaryAmount'
                    AND upper(regexp_replace(m."Value", '[^[:alnum:]]', '', 'g')) =
                        ANY(@monetary_values::text[])) AS "OtherExactMatch"
            FROM "DocumentMetadataEntries" AS m
            INNER JOIN eligible_documents AS e
                ON e."DocumentId" = m."DocumentUnderstandingId"
        ),
        scored_documents AS (
            SELECT
                e.*,
                COALESCE(
                    e."DocumentType" = ANY(@document_types::text[]),
                    FALSE) AS "DocumentTypeMatched",
                lower(e."DocumentName") = @normalized_query
                    OR lower(regexp_replace(e."DocumentName", '\.[^.]+$', '')) =
                        @normalized_query
                    OR EXISTS (
                        SELECT 1
                        FROM unnest(@search_terms::text[]) AS query_term(value)
                        WHERE to_tsvector('simple', e."DocumentName")
                              @@ plainto_tsquery('simple', query_term.value)
                    ) AS "FileNameMatched",
                e."DetectedTitle" IS NOT NULL AND (
                    lower(e."DetectedTitle") = @normalized_query
                    OR EXISTS (
                        SELECT 1
                        FROM unnest(@search_terms::text[]) AS query_term(value)
                        WHERE to_tsvector('simple', e."DetectedTitle")
                              @@ plainto_tsquery('simple', query_term.value)
                    )) AS "DetectedTitleMatched",
                (
                    CASE
                        WHEN e."DocumentType" = ANY(@document_types::text[])
                            THEN 4.0::double precision
                        ELSE 0.0::double precision
                    END
                    + CASE
                        WHEN lower(e."DocumentName") = @normalized_query
                             OR lower(regexp_replace(e."DocumentName", '\.[^.]+$', '')) =
                                @normalized_query
                            THEN 6.0::double precision
                        ELSE 1.25::double precision * (
                            SELECT count(*)::double precision
                            FROM unnest(@search_terms::text[]) AS query_term(value)
                            WHERE to_tsvector('simple', e."DocumentName")
                                  @@ plainto_tsquery('simple', query_term.value)
                        )
                    END
                    + CASE
                        WHEN e."DetectedTitle" IS NOT NULL
                             AND lower(e."DetectedTitle") = @normalized_query
                            THEN 5.0::double precision
                        ELSE 1.0::double precision * (
                            SELECT count(*)::double precision
                            FROM unnest(@search_terms::text[]) AS query_term(value)
                            WHERE e."DetectedTitle" IS NOT NULL
                              AND to_tsvector('simple', e."DetectedTitle")
                                  @@ plainto_tsquery('simple', query_term.value)
                        )
                    END
                    + 0.75::double precision * (
                        SELECT count(*)::double precision
                        FROM unnest(@search_terms::text[]) AS query_term(value)
                        WHERE e."DocumentSubtype" IS NOT NULL
                          AND to_tsvector('simple', e."DocumentSubtype")
                              @@ plainto_tsquery('simple', query_term.value)
                    )
                    + 0.5::double precision * (
                        SELECT count(*)::double precision
                        FROM unnest(@search_terms::text[]) AS query_term(value)
                        WHERE e."Subject" IS NOT NULL
                          AND to_tsvector('simple', e."Subject")
                              @@ plainto_tsquery('simple', query_term.value)
                    )
                    + LEAST(
                        COALESCE((
                            SELECT sum(metadata_match."MatchScore")
                            FROM metadata_matches AS metadata_match
                            WHERE metadata_match."DocumentId" = e."DocumentId"
                              AND metadata_match."MatchScore" > 0
                        ), 0.0::double precision),
                        16.0::double precision)
                )::double precision AS "MatchScore",
                EXISTS (
                    SELECT 1
                    FROM metadata_matches AS metadata_match
                    WHERE metadata_match."DocumentId" = e."DocumentId"
                      AND metadata_match."ExactIdentifierMatch"
                ) AS "HasExactIdentifierMatch"
            FROM eligible_documents AS e
        ),
        matching_documents AS (
            SELECT *
            FROM scored_documents
            WHERE "MatchScore" > 0
        ),
        ranked_documents AS (
            SELECT
                matching_documents.*,
                row_number() OVER (
                    ORDER BY "MatchScore" DESC, "DocumentId") AS "MatchRank"
            FROM matching_documents
            ORDER BY "MatchScore" DESC, "DocumentId"
            LIMIT @candidate_count
        )
        SELECT
            ranked_document."DocumentId",
            ranked_document."DocumentName",
            ranked_document."DocumentType",
            ranked_document."DetectedTitle",
            ranked_document."MatchScore",
            ranked_document."MatchRank",
            ranked_document."HasExactIdentifierMatch",
            ranked_document."DocumentTypeMatched",
            ranked_document."FileNameMatched",
            ranked_document."DetectedTitleMatched",
            metadata_match."Kind",
            metadata_match."Value",
            metadata_match."ExactIdentifierMatch",
            metadata_match."OtherExactMatch"
        FROM ranked_documents AS ranked_document
        LEFT JOIN LATERAL (
            SELECT
                candidate."Kind",
                candidate."Value",
                candidate."ExactIdentifierMatch",
                candidate."OtherExactMatch",
                candidate."MatchScore",
                candidate."Sequence",
                candidate."Id"
            FROM metadata_matches AS candidate
            WHERE candidate."DocumentId" = ranked_document."DocumentId"
              AND candidate."MatchScore" > 0
            ORDER BY
                candidate."ExactIdentifierMatch" DESC,
                candidate."OtherExactMatch" DESC,
                candidate."MatchScore" DESC,
                candidate."Sequence",
                candidate."Id"
            LIMIT @metadata_summary_count
        ) AS metadata_match ON TRUE
        ORDER BY
            ranked_document."MatchRank",
            metadata_match."ExactIdentifierMatch" DESC NULLS LAST,
            metadata_match."OtherExactMatch" DESC NULLS LAST,
            metadata_match."MatchScore" DESC NULLS LAST,
            metadata_match."Sequence" NULLS LAST,
            metadata_match."Id" NULLS LAST;
        """;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<PostgreSqlMetadataDocumentSearch> _logger;

    public PostgreSqlMetadataDocumentSearch(
        ApplicationDbContext dbContext,
        ILogger<PostgreSqlMetadataDocumentSearch> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MetadataDocumentMatch>> SearchAsync(
        Guid ownerId,
        Guid projectId,
        RetrievalQuery query,
        int candidateCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(candidateCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            candidateCount,
            SemanticRetrievalLimits.MaximumMetadataDocumentCandidateCount);

        if (query.SearchTerms.Count == 0 &&
            query.DocumentTypeHints.Count == 0 &&
            query.IdentifierValues.Count == 0 &&
            query.DateValues.Count == 0 &&
            query.MonetaryValues.Count == 0)
        {
            return [];
        }

        var connection = _dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        try
        {
            if (shouldCloseConnection)
            {
                await _dbContext.Database.OpenConnectionAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = SearchSql;
            AddParameter(command, "project_id", projectId);
            AddParameter(command, "owner_id", ownerId);
            AddParameter(command, "ready_status", DocumentStatus.Ready.ToString());
            AddParameter(
                command,
                "understanding_ready_status",
                DocumentUnderstandingStatus.Ready.ToString());
            AddParameter(command, "normalized_query", query.NormalizedText);
            AddParameter(command, "search_terms", query.SearchTerms.ToArray());
            AddParameter(
                command,
                "document_types",
                query.DocumentTypeHints.Select(value => value.ToString()).ToArray());
            AddParameter(command, "identifier_values", query.IdentifierValues.ToArray());
            AddParameter(command, "date_values", query.DateValues.ToArray());
            AddParameter(command, "monetary_values", query.MonetaryValues.ToArray());
            AddParameter(command, "candidate_count", candidateCount);
            AddParameter(
                command,
                "metadata_summary_count",
                SemanticRetrievalLimits.MaximumMatchedMetadataSignals);

            var builders = new Dictionary<Guid, MatchBuilder>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var documentId = reader.GetGuid(0);
                if (!builders.TryGetValue(documentId, out var builder))
                {
                    builder = new MatchBuilder(
                        documentId,
                        reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.GetDouble(4),
                        checked((int)reader.GetInt64(5)),
                        reader.GetBoolean(6),
                        reader.GetBoolean(7),
                        reader.GetBoolean(8),
                        reader.GetBoolean(9));
                    builders.Add(documentId, builder);
                }

                if (!reader.IsDBNull(10))
                {
                    builder.AddMetadata(
                        reader.GetString(10),
                        reader.GetString(11),
                        reader.GetBoolean(12) || reader.GetBoolean(13));
                }
            }

            return builders.Values
                .OrderBy(value => value.Rank)
                .Select(value => value.Build())
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "Metadata document search failed for project {ProjectId} with candidate bound {CandidateCount} and exception type {ExceptionType}. Query text and metadata values were omitted.",
                projectId,
                candidateCount,
                exception.GetType().FullName);
            throw new SemanticRetrievalException(
                "Document search could not be completed. Please try again.",
                exception);
        }
        finally
        {
            if (shouldCloseConnection && connection.State == ConnectionState.Open)
            {
                await _dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed class MatchBuilder
    {
        private readonly Guid _documentId;
        private readonly string _documentName;
        private readonly string? _documentType;
        private readonly string? _detectedTitle;
        private readonly double _matchScore;
        private readonly bool _hasExactIdentifierMatch;
        private readonly bool _documentTypeMatched;
        private readonly bool _fileNameMatched;
        private readonly bool _detectedTitleMatched;
        private readonly List<MatchedRetrievalMetadata> _metadata = [];

        public MatchBuilder(
            Guid documentId,
            string documentName,
            string? documentType,
            string? detectedTitle,
            double matchScore,
            int rank,
            bool hasExactIdentifierMatch,
            bool documentTypeMatched,
            bool fileNameMatched,
            bool detectedTitleMatched)
        {
            _documentId = documentId;
            _documentName = documentName;
            _documentType = documentType;
            _detectedTitle = detectedTitle;
            _matchScore = matchScore;
            Rank = rank;
            _hasExactIdentifierMatch = hasExactIdentifierMatch;
            _documentTypeMatched = documentTypeMatched;
            _fileNameMatched = fileNameMatched;
            _detectedTitleMatched = detectedTitleMatched;
        }

        public int Rank { get; }

        public void AddMetadata(string kind, string value, bool isExact)
        {
            if (_metadata.Count >= SemanticRetrievalLimits.MaximumMatchedMetadataSignals ||
                _metadata.Any(item =>
                    string.Equals(item.Field, kind, StringComparison.Ordinal) &&
                    string.Equals(item.Value, value, StringComparison.Ordinal)))
            {
                return;
            }

            _metadata.Add(new MatchedRetrievalMetadata(kind, value, isExact));
        }

        public MetadataDocumentMatch Build()
        {
            AddDocumentSignal("DocumentType", _documentType, _documentTypeMatched);
            AddDocumentSignal("DetectedTitle", _detectedTitle, _detectedTitleMatched);
            AddDocumentSignal("FileName", _documentName, _fileNameMatched);

            return new MetadataDocumentMatch(
                _documentId,
                Rank,
                _matchScore,
                _hasExactIdentifierMatch,
                _metadata.ToArray());
        }

        private void AddDocumentSignal(string field, string? value, bool matched)
        {
            if (!matched || string.IsNullOrWhiteSpace(value) ||
                _metadata.Count >= SemanticRetrievalLimits.MaximumMatchedMetadataSignals)
            {
                return;
            }

            _metadata.Add(new MatchedRetrievalMetadata(field, value, false));
        }
    }
}
