using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.DocumentAssistant.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentNormalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NormalizationChanged",
                table: "DocumentTextSections",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "NormalizedAtUtc",
                table: "DocumentTextSections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedContent",
                table: "DocumentTextSections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RemovedCharacterCount",
                table: "DocumentTextSections",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NormalizationChangedSectionCount",
                table: "Documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "NormalizationError",
                table: "Documents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "NormalizationRemovedCharacterCount",
                table: "Documents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "NormalizedAtUtc",
                table: "Documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "NormalizedCharacterCount",
                table: "Documents",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NormalizationChanged",
                table: "DocumentTextSections");

            migrationBuilder.DropColumn(
                name: "NormalizedAtUtc",
                table: "DocumentTextSections");

            migrationBuilder.DropColumn(
                name: "NormalizedContent",
                table: "DocumentTextSections");

            migrationBuilder.DropColumn(
                name: "RemovedCharacterCount",
                table: "DocumentTextSections");

            migrationBuilder.DropColumn(
                name: "NormalizationChangedSectionCount",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "NormalizationError",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "NormalizationRemovedCharacterCount",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "NormalizedAtUtc",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "NormalizedCharacterCount",
                table: "Documents");
        }
    }
}
