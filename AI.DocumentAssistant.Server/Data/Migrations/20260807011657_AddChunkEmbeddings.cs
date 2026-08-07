using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace AI.DocumentAssistant.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChunkEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.AddColumn<DateTime>(
                name: "EmbeddedAtUtc",
                table: "Documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmbeddedChunkCount",
                table: "Documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EmbeddingDimensions",
                table: "Documents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingError",
                table: "Documents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingModel",
                table: "Documents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmbeddedAtUtc",
                table: "DocumentChunks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Vector>(
                name: "Embedding",
                table: "DocumentChunks",
                type: "vector(1536)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingContentHash",
                table: "DocumentChunks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmbeddingDimensions",
                table: "DocumentChunks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingModel",
                table: "DocumentChunks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmbeddedAtUtc",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "EmbeddedChunkCount",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "EmbeddingDimensions",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "EmbeddingError",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "EmbeddingModel",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "EmbeddedAtUtc",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "Embedding",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "EmbeddingContentHash",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "EmbeddingDimensions",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "EmbeddingModel",
                table: "DocumentChunks");

            // Keep the database-level vector extension enabled on rollback. It may have
            // existed before this migration and can be shared by other database objects.
        }
    }
}
