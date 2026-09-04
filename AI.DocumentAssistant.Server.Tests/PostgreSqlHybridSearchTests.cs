using AI.DocumentAssistant.Server.Retrieval;
using AI.DocumentAssistant.Server.Data;
using AI.DocumentAssistant.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using NpgsqlTypes;
using Pgvector.EntityFrameworkCore;

namespace AI.DocumentAssistant.Server.Tests;

public sealed class PostgreSqlHybridSearchTests
{
    [Fact]
    public void DocumentChunkUsesGeneratedSimpleTsVectorWithGinIndex()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=model-inspection;Username=test;Password=test",
                npgsqlOptions => npgsqlOptions.UseVector())
            .Options;
        using var context = new ApplicationDbContext(options);
        var entity = context.GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(DocumentChunk));
        var property = entity!.FindProperty(nameof(DocumentChunk.SearchVector));
        var index = entity.GetIndexes().Single(value =>
            value.Properties.SequenceEqual([property!]));

        Assert.Equal(typeof(NpgsqlTsVector), property!.ClrType);
        Assert.Equal("simple", property.FindAnnotation("Npgsql:TsVectorConfig")!.Value);
        Assert.Equal(
            new[] { nameof(DocumentChunk.Content) },
            property.FindAnnotation("Npgsql:TsVectorProperties")!.Value);
        Assert.Equal("GIN", index.FindAnnotation("Npgsql:IndexMethod")!.Value);
    }

    [Fact]
    public void LexicalSearchUsesSafeSimpleWebSearchAndStableBoundedRanking()
    {
        var sql = PostgreSqlLexicalChunkSearch.SearchSql;

        Assert.Contains("websearch_to_tsquery('simple', @query_text)", sql, StringComparison.Ordinal);
        Assert.Contains("websearch_to_tsquery('simple', @relaxed_query_text)", sql, StringComparison.Ordinal);
        Assert.Contains("c.\"SearchVector\" @@ prepared_query.original_value", sql, StringComparison.Ordinal);
        Assert.Contains("c.\"SearchVector\" @@ prepared_query.relaxed_value", sql, StringComparison.Ordinal);
        Assert.Contains("ts_rank_cd(c.\"SearchVector\", prepared_query.original_value)", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT @candidate_count", sql, StringComparison.Ordinal);
        Assert.Contains("d.\"Id\"", sql, StringComparison.Ordinal);
        Assert.Contains("c.\"ChunkIndex\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("to_tsquery", sql.Replace("websearch_to_tsquery", string.Empty), StringComparison.Ordinal);
    }

    [Fact]
    public void LexicalSearchEnforcesOwnerProjectAndReadyStatusInsideSql()
    {
        var sql = PostgreSqlLexicalChunkSearch.SearchSql;

        Assert.Contains("p.\"OwnerId\" = @owner_id", sql, StringComparison.Ordinal);
        Assert.Contains("p.\"Id\" = @project_id", sql, StringComparison.Ordinal);
        Assert.Contains("d.\"ProjectId\" = @project_id", sql, StringComparison.Ordinal);
        Assert.Contains("d.\"Status\" = @ready_status", sql, StringComparison.Ordinal);
        Assert.True(
            sql.IndexOf("p.\"OwnerId\" = @owner_id", StringComparison.Ordinal) <
            sql.IndexOf("ORDER BY", StringComparison.Ordinal));
    }

    [Fact]
    public void MetadataSearchScopesEligibleUnderstandingsBeforeMetadataMatching()
    {
        var sql = PostgreSqlMetadataDocumentSearch.SearchSql;

        Assert.Contains("p.\"OwnerId\" = @owner_id", sql, StringComparison.Ordinal);
        Assert.Contains("p.\"Id\" = @project_id", sql, StringComparison.Ordinal);
        Assert.Contains("d.\"ProjectId\" = @project_id", sql, StringComparison.Ordinal);
        Assert.Contains("d.\"Status\" = @ready_status", sql, StringComparison.Ordinal);
        Assert.Contains("u.\"Status\" = @understanding_ready_status", sql, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN eligible_documents AS e", sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT @candidate_count", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("Technical", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("Ocr", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MetadataSearchUsesExactStructuredValuesAndParameterizedTermArrays()
    {
        var sql = PostgreSqlMetadataDocumentSearch.SearchSql;

        Assert.Contains("ANY(@identifier_values::text[])", sql, StringComparison.Ordinal);
        Assert.Contains("ANY(@date_values::text[])", sql, StringComparison.Ordinal);
        Assert.Contains("ANY(@monetary_values::text[])", sql, StringComparison.Ordinal);
        Assert.Contains("unnest(@search_terms::text[])", sql, StringComparison.Ordinal);
        Assert.Contains("ANY(@document_types::text[])", sql, StringComparison.Ordinal);
        Assert.Contains("plainto_tsquery('simple', query_term.value)", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("foo OR bar")]
    [InlineData("CN-2026-00491")]
    [InlineData("what's in section (4.2)?")]
    [InlineData("\"quoted phrase\"")]
    [InlineData("INV/2026/118")]
    [InlineData("șțăîâ 東京")]
    public void UserInputIsNeverInterpolatedIntoLexicalOrMetadataSql(string query)
    {
        Assert.DoesNotContain(query, PostgreSqlLexicalChunkSearch.SearchSql, StringComparison.Ordinal);
        Assert.DoesNotContain(query, PostgreSqlMetadataDocumentSearch.SearchSql, StringComparison.Ordinal);
        Assert.Contains("@query_text", PostgreSqlLexicalChunkSearch.SearchSql, StringComparison.Ordinal);
        Assert.Contains("@normalized_query", PostgreSqlMetadataDocumentSearch.SearchSql, StringComparison.Ordinal);
    }
}
