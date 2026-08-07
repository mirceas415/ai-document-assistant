using System.Data;
using System.Data.Common;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Models;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace AI.DocumentAssistant.Server.Retrieval;

public sealed class PgvectorSemanticChunkSearch : ISemanticChunkSearch
{
    public const string SearchSql = """
        SELECT
            d."Id" AS "DocumentId",
            d."OriginalFileName" AS "DocumentName",
            c."Id" AS "ChunkId",
            c."ChunkIndex",
            c."Content",
            c."PageStart",
            c."PageEnd",
            c."SectionTitle" AS "Heading",
            c."Embedding" <=> @query_embedding AS "CosineDistance"
        FROM "DocumentChunks" AS c
        INNER JOIN "Documents" AS d ON d."Id" = c."DocumentId"
        INNER JOIN "Projects" AS p ON p."Id" = d."ProjectId"
        WHERE p."Id" = @project_id
          AND p."OwnerId" = @owner_id
          AND d."ProjectId" = @project_id
          AND d."Status" = @ready_status
          AND d."ChunkCount" > 0
          AND d."EmbeddedChunkCount" = d."ChunkCount"
          AND d."EmbeddingModel" = @embedding_model
          AND d."EmbeddingDimensions" = @embedding_dimensions
          AND d."EmbeddedAtUtc" IS NOT NULL
          AND c."Embedding" IS NOT NULL
          AND c."EmbeddingModel" = @embedding_model
          AND c."EmbeddingDimensions" = @embedding_dimensions
          AND c."EmbeddedAtUtc" = d."EmbeddedAtUtc"
          AND c."EmbeddingContentHash" = upper(
              encode(sha256(convert_to(c."Content", 'UTF8')), 'hex'))
        ORDER BY c."Embedding" <=> @query_embedding
        LIMIT @top_k;
        """;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<PgvectorSemanticChunkSearch> _logger;

    public PgvectorSemanticChunkSearch(
        ApplicationDbContext dbContext,
        ILogger<PgvectorSemanticChunkSearch> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RetrievedDocumentChunk>> SearchAsync(
        Guid ownerId,
        Guid projectId,
        Vector queryEmbedding,
        string embeddingModel,
        int embeddingDimensions,
        int topK,
        CancellationToken cancellationToken)
    {
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
            AddParameter(command, "query_embedding", queryEmbedding);
            AddParameter(command, "project_id", projectId);
            AddParameter(command, "owner_id", ownerId);
            AddParameter(command, "ready_status", DocumentStatus.Ready.ToString());
            AddParameter(command, "embedding_model", embeddingModel);
            AddParameter(command, "embedding_dimensions", embeddingDimensions);
            AddParameter(command, "top_k", topK);

            var results = new List<RetrievedDocumentChunk>(topK);
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
                    reader.GetDouble(8)));
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
                "Semantic vector query failed for project {ProjectId} (TopK {TopK}) with exception type {ExceptionType}. Query text, document content, and vector values were omitted.",
                projectId,
                topK,
                exception.GetType().FullName);
            throw new SemanticRetrievalException(
                "Semantic search could not be completed. Please try again.",
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

    private static void AddParameter(
        DbCommand command,
        string name,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
