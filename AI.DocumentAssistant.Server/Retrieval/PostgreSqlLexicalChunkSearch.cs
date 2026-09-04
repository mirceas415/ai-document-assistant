using System.Data;
using System.Data.Common;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.Server.Retrieval;

public sealed class PostgreSqlLexicalChunkSearch : ILexicalChunkSearch
{
    public const string SearchSql = """
        WITH prepared_query AS (
            SELECT
                websearch_to_tsquery('simple', @query_text) AS original_value,
                websearch_to_tsquery('simple', @relaxed_query_text) AS relaxed_value
        )
        SELECT
            d."Id" AS "DocumentId",
            d."OriginalFileName" AS "DocumentName",
            c."Id" AS "ChunkId",
            c."ChunkIndex",
            c."Content",
            c."PageStart",
            c."PageEnd",
            c."SectionTitle" AS "Heading",
            (ts_rank_cd(c."SearchVector", prepared_query.original_value)
             + ts_rank_cd(c."SearchVector", prepared_query.relaxed_value))::real
                AS "LexicalRankScore"
        FROM "DocumentChunks" AS c
        INNER JOIN "Documents" AS d ON d."Id" = c."DocumentId"
        INNER JOIN "Projects" AS p ON p."Id" = d."ProjectId"
        CROSS JOIN prepared_query
        WHERE p."Id" = @project_id
          AND p."OwnerId" = @owner_id
          AND d."ProjectId" = @project_id
          AND d."Status" = @ready_status
          AND d."ChunkCount" > 0
          AND (c."SearchVector" @@ prepared_query.original_value
               OR c."SearchVector" @@ prepared_query.relaxed_value)
        ORDER BY
            (ts_rank_cd(c."SearchVector", prepared_query.original_value)
             + ts_rank_cd(c."SearchVector", prepared_query.relaxed_value)) DESC,
            d."Id",
            c."ChunkIndex",
            c."Id"
        LIMIT @candidate_count;
        """;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<PostgreSqlLexicalChunkSearch> _logger;

    public PostgreSqlLexicalChunkSearch(
        ApplicationDbContext dbContext,
        ILogger<PostgreSqlLexicalChunkSearch> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RetrievedDocumentChunk>> SearchAsync(
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
            SemanticRetrievalLimits.MaximumCandidateCount);

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
            AddParameter(command, "query_text", query.OriginalText);
            AddParameter(
                command,
                "relaxed_query_text",
                string.Join(' ', query.SearchTerms));
            AddParameter(command, "project_id", projectId);
            AddParameter(command, "owner_id", ownerId);
            AddParameter(command, "ready_status", DocumentStatus.Ready.ToString());
            AddParameter(command, "candidate_count", candidateCount);

            var results = new List<RetrievedDocumentChunk>(candidateCount);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new RetrievedDocumentChunk(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetGuid(2),
                    reader.GetInt32(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    null,
                    LexicalRankScore: reader.GetFloat(8)));
            }

            return results;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                "PostgreSQL lexical search failed for project {ProjectId} with candidate bound {CandidateCount} and exception type {ExceptionType}. Query text and document content were omitted.",
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
}
