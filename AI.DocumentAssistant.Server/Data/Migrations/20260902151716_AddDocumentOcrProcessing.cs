using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.DocumentAssistant.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentOcrProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtractionMethod",
                table: "DocumentTextSections",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.CreateTable(
                name: "DocumentOcrAnalyses",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CandidatePageCount = table.Column<int>(type: "integer", nullable: false),
                    SuccessfulPageCount = table.Column<int>(type: "integer", nullable: false),
                    FailedPageCount = table.Column<int>(type: "integer", nullable: false),
                    EngineName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EngineVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Languages = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RenderDpi = table.Column<int>(type: "integer", nullable: true),
                    MaxCandidatePages = table.Column<int>(type: "integer", nullable: true),
                    MaxRenderedPixels = table.Column<long>(type: "bigint", nullable: true),
                    SourceFileHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RoutingVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RoutingHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ConfigurationHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentOcrAnalyses", x => x.DocumentId);
                    table.ForeignKey(
                        name: "FK_DocumentOcrAnalyses_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentPageOcrResults",
                columns: table => new
                {
                    DocumentOcrAnalysisId = table.Column<Guid>(type: "uuid", nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceTechnicalType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RecognizedCharacterCount = table.Column<int>(type: "integer", nullable: false),
                    RecognizedWordCount = table.Column<int>(type: "integer", nullable: false),
                    MeanConfidence = table.Column<double>(type: "double precision", nullable: true),
                    EffectiveRenderDpi = table.Column<int>(type: "integer", nullable: true),
                    RenderedWidthPixels = table.Column<int>(type: "integer", nullable: true),
                    RenderedHeightPixels = table.Column<int>(type: "integer", nullable: true),
                    ProcessingDurationMs = table.Column<long>(type: "bigint", nullable: true),
                    UsedInExtraction = table.Column<bool>(type: "boolean", nullable: false),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentPageOcrResults", x => new { x.DocumentOcrAnalysisId, x.PageNumber });
                    table.CheckConstraint("CK_DocumentPageOcrResults_MeanConfidence", "\"MeanConfidence\" IS NULL OR (\"MeanConfidence\" >= 0 AND \"MeanConfidence\" <= 1)");
                    table.ForeignKey(
                        name: "FK_DocumentPageOcrResults_DocumentOcrAnalyses_DocumentOcrAnaly~",
                        column: x => x.DocumentOcrAnalysisId,
                        principalTable: "DocumentOcrAnalyses",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentPageOcrResults");

            migrationBuilder.DropTable(
                name: "DocumentOcrAnalyses");

            migrationBuilder.DropColumn(
                name: "ExtractionMethod",
                table: "DocumentTextSections");
        }
    }
}
