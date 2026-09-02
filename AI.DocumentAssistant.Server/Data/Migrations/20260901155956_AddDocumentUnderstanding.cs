using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.DocumentAssistant.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentUnderstanding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentUnderstandings",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    DocumentSubtype = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DocumentTypeConfidence = table.Column<double>(type: "double precision", nullable: true),
                    PrimaryLanguageCode = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: true),
                    LanguageConfidence = table.Column<double>(type: "double precision", nullable: true),
                    DetectedTitle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PromptVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourceContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AnalyzedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentUnderstandings", x => x.DocumentId);
                    table.CheckConstraint("CK_DocumentUnderstandings_DocumentTypeConfidence", "\"DocumentTypeConfidence\" IS NULL OR (\"DocumentTypeConfidence\" >= 0 AND \"DocumentTypeConfidence\" <= 1)");
                    table.CheckConstraint("CK_DocumentUnderstandings_LanguageConfidence", "\"LanguageConfidence\" IS NULL OR (\"LanguageConfidence\" >= 0 AND \"LanguageConfidence\" <= 1)");
                    table.ForeignKey(
                        name: "FK_DocumentUnderstandings_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentMetadataEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentUnderstandingId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    NormalizedValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Confidence = table.Column<double>(type: "double precision", nullable: true),
                    Sequence = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentMetadataEntries", x => x.Id);
                    table.CheckConstraint("CK_DocumentMetadataEntries_Confidence", "\"Confidence\" IS NULL OR (\"Confidence\" >= 0 AND \"Confidence\" <= 1)");
                    table.ForeignKey(
                        name: "FK_DocumentMetadataEntries_DocumentUnderstandings_DocumentUnde~",
                        column: x => x.DocumentUnderstandingId,
                        principalTable: "DocumentUnderstandings",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentMetadataEntries_DocumentUnderstandingId_Sequence",
                table: "DocumentMetadataEntries",
                columns: new[] { "DocumentUnderstandingId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentMetadataEntries_Kind_Label",
                table: "DocumentMetadataEntries",
                columns: new[] { "Kind", "Label" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentMetadataEntries");

            migrationBuilder.DropTable(
                name: "DocumentUnderstandings");
        }
    }
}
