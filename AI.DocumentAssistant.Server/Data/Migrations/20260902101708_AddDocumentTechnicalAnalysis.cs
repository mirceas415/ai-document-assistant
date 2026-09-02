using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.DocumentAssistant.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentTechnicalAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentTechnicalAnalyses",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TechnicalType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PageCount = table.Column<int>(type: "integer", nullable: false),
                    TextBasedPageCount = table.Column<int>(type: "integer", nullable: false),
                    ScannedPageCount = table.Column<int>(type: "integer", nullable: false),
                    ImageBasedPageCount = table.Column<int>(type: "integer", nullable: false),
                    MixedPageCount = table.Column<int>(type: "integer", nullable: false),
                    UnknownPageCount = table.Column<int>(type: "integer", nullable: false),
                    SourceFileHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AnalyzerVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AnalyzedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTechnicalAnalyses", x => x.DocumentId);
                    table.ForeignKey(
                        name: "FK_DocumentTechnicalAnalyses_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentPageTechnicalAnalyses",
                columns: table => new
                {
                    DocumentTechnicalAnalysisId = table.Column<Guid>(type: "uuid", nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: false),
                    TechnicalType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TextCharacterCount = table.Column<int>(type: "integer", nullable: false),
                    WordCount = table.Column<int>(type: "integer", nullable: false),
                    ImageCount = table.Column<int>(type: "integer", nullable: false),
                    ImageCoverageRatio = table.Column<double>(type: "double precision", nullable: false),
                    HasMeaningfulText = table.Column<bool>(type: "boolean", nullable: false),
                    HasPageSizedImage = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentPageTechnicalAnalyses", x => new { x.DocumentTechnicalAnalysisId, x.PageNumber });
                    table.CheckConstraint("CK_DocumentPageTechnicalAnalyses_ImageCoverageRatio", "\"ImageCoverageRatio\" >= 0 AND \"ImageCoverageRatio\" <= 1");
                    table.ForeignKey(
                        name: "FK_DocumentPageTechnicalAnalyses_DocumentTechnicalAnalyses_Doc~",
                        column: x => x.DocumentTechnicalAnalysisId,
                        principalTable: "DocumentTechnicalAnalyses",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentPageTechnicalAnalyses");

            migrationBuilder.DropTable(
                name: "DocumentTechnicalAnalyses");
        }
    }
}
