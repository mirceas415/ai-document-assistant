using AI.DocumentAssistant.Server.Embeddings;
using AI.DocumentAssistant.Server.Models;
using AI.DocumentAssistant.Server.Ocr;
using AI.DocumentAssistant.Server.TechnicalAnalysis;
using AI.DocumentAssistant.Server.Understanding;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.Server.Data;

public sealed class ApplicationDbContext
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentTextSection> DocumentTextSections => Set<DocumentTextSection>();

    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();

    public DbSet<DocumentUnderstanding> DocumentUnderstandings =>
        Set<DocumentUnderstanding>();

    public DbSet<DocumentMetadataEntry> DocumentMetadataEntries =>
        Set<DocumentMetadataEntry>();

    public DbSet<DocumentTechnicalAnalysis> DocumentTechnicalAnalyses =>
        Set<DocumentTechnicalAnalysis>();

    public DbSet<DocumentPageTechnicalAnalysis> DocumentPageTechnicalAnalyses =>
        Set<DocumentPageTechnicalAnalysis>();

    public DbSet<DocumentOcrAnalysis> DocumentOcrAnalyses =>
        Set<DocumentOcrAnalysis>();

    public DbSet<DocumentPageOcrResult> DocumentPageOcrResults =>
        Set<DocumentPageOcrResult>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<ConversationMessage> ConversationMessages =>
        Set<ConversationMessage>();

    public DbSet<ConversationMessageSource> ConversationMessageSources =>
        Set<ConversationMessageSource>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasPostgresExtension("vector");

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(value => value.DisplayName)
                .HasMaxLength(100)
                .IsRequired();

            user.Property(value => value.CreatedAtUtc)
                .IsRequired();

            user.HasIndex(value => value.NormalizedEmail)
                .HasDatabaseName("EmailIndex")
                .IsUnique();
        });

        builder.Entity<Project>(project =>
        {
            project.ToTable("Projects");

            project.HasKey(value => value.Id);

            project.Property(value => value.Name)
                .HasMaxLength(100)
                .IsRequired();

            project.Property(value => value.Description)
                .HasMaxLength(1_000);

            project.Property(value => value.CreatedAtUtc)
                .IsRequired();

            project.Property(value => value.UpdatedAtUtc)
                .IsRequired();

            project.HasOne(value => value.Owner)
                .WithMany(user => user.OwnedProjects)
                .HasForeignKey(value => value.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            project.HasIndex(value => new { value.OwnerId, value.UpdatedAtUtc });
        });

        builder.Entity<Document>(document =>
        {
            document.ToTable("Documents");

            document.HasKey(value => value.Id);

            document.Property(value => value.OriginalFileName)
                .HasMaxLength(255)
                .IsRequired();

            document.Property(value => value.StoredFileName)
                .HasMaxLength(100)
                .IsRequired();

            document.Property(value => value.ContentType)
                .HasMaxLength(150)
                .IsRequired();

            document.Property(value => value.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            document.Property(value => value.ProcessingError)
                .HasMaxLength(500);

            document.Property(value => value.ChunkingError)
                .HasMaxLength(500);

            document.Property(value => value.EmbeddingModel)
                .HasMaxLength(EmbeddingArchitecture.MaximumModelNameLength);

            document.Property(value => value.EmbeddingError)
                .HasMaxLength(500);

            document.Property(value => value.NormalizationError)
                .HasMaxLength(500);

            document.Property(value => value.CreatedAtUtc)
                .IsRequired();

            document.Property(value => value.UpdatedAtUtc)
                .IsRequired();

            document.HasOne(value => value.Project)
                .WithMany(project => project.Documents)
                .HasForeignKey(value => value.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            document.HasIndex(value => new { value.ProjectId, value.CreatedAtUtc });
        });

        builder.Entity<DocumentTextSection>(section =>
        {
            section.ToTable("DocumentTextSections");

            section.HasKey(value => value.Id);

            section.Property(value => value.Content)
                .HasColumnType("text")
                .IsRequired();

            section.Property(value => value.NormalizedContent)
                .HasColumnType("text");

            section.Property(value => value.SectionTitle)
                .HasMaxLength(500);

            section.Property(value => value.ExtractionMethod)
                .HasConversion<string>()
                .HasMaxLength(32)
                .HasDefaultValue(DocumentTextExtractionMethod.Unknown)
                .IsRequired();

            section.Property(value => value.CreatedAtUtc)
                .IsRequired();

            section.HasOne(value => value.Document)
                .WithMany(document => document.TextSections)
                .HasForeignKey(value => value.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            section.HasIndex(value => new { value.DocumentId, value.SectionIndex })
                .IsUnique();
        });

        builder.Entity<DocumentChunk>(chunk =>
        {
            chunk.ToTable("DocumentChunks");

            chunk.HasKey(value => value.Id);

            chunk.Property(value => value.Content)
                .HasColumnType("text")
                .IsRequired();

            if (string.Equals(
                    Database.ProviderName,
                    "Microsoft.EntityFrameworkCore.InMemory",
                    StringComparison.Ordinal))
            {
                chunk.Ignore(value => value.SearchVector);
            }
            else
            {
                chunk.HasGeneratedTsVectorColumn(
                        value => value.SearchVector,
                        "simple",
                        value => new { value.Content })
                    .HasIndex(value => value.SearchVector)
                    .HasDatabaseName("IX_DocumentChunks_SearchVector")
                    .HasMethod("GIN");
            }

            chunk.Property(value => value.SectionTitle)
                .HasMaxLength(500);

            var embeddingProperty = chunk.Property(value => value.Embedding);
            if (string.Equals(
                    Database.ProviderName,
                    "Microsoft.EntityFrameworkCore.InMemory",
                    StringComparison.Ordinal))
            {
                // The test provider has no pgvector type mapping. This converter is only
                // used by offline InMemory tests; PostgreSQL always uses Pgvector.Vector
                // and a native vector(1536) column.
                embeddingProperty.HasConversion(
                    embedding => SerializeEmbeddingForInMemory(embedding),
                    bytes => DeserializeEmbeddingForInMemory(bytes));
            }
            else
            {
                embeddingProperty.HasColumnType(
                    $"vector({EmbeddingArchitecture.Dimensions})");
            }

            chunk.Property(value => value.EmbeddingModel)
                .HasMaxLength(EmbeddingArchitecture.MaximumModelNameLength);

            chunk.Property(value => value.EmbeddingContentHash)
                .HasMaxLength(EmbeddingArchitecture.ContentHashLength);

            chunk.Property(value => value.CreatedAtUtc)
                .IsRequired();

            chunk.HasOne(value => value.Document)
                .WithMany(document => document.Chunks)
                .HasForeignKey(value => value.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            chunk.HasIndex(value => value.DocumentId);

            chunk.HasIndex(value => new { value.DocumentId, value.ChunkIndex })
                .IsUnique();
        });

        builder.Entity<DocumentUnderstanding>(understanding =>
        {
            understanding.ToTable("DocumentUnderstandings", table =>
            {
                table.HasCheckConstraint(
                    "CK_DocumentUnderstandings_DocumentTypeConfidence",
                    "\"DocumentTypeConfidence\" IS NULL OR (\"DocumentTypeConfidence\" >= 0 AND \"DocumentTypeConfidence\" <= 1)");
                table.HasCheckConstraint(
                    "CK_DocumentUnderstandings_LanguageConfidence",
                    "\"LanguageConfidence\" IS NULL OR (\"LanguageConfidence\" >= 0 AND \"LanguageConfidence\" <= 1)");
            });

            understanding.HasKey(value => value.DocumentId);

            understanding.Property(value => value.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            understanding.Property(value => value.DocumentType)
                .HasConversion<string>()
                .HasMaxLength(32);

            understanding.Property(value => value.DocumentSubtype)
                .HasMaxLength(DocumentUnderstandingLimits.MaximumDocumentSubtypeLength);

            understanding.Property(value => value.PrimaryLanguageCode)
                .HasMaxLength(DocumentUnderstandingLimits.MaximumLanguageCodeLength);

            understanding.Property(value => value.DetectedTitle)
                .HasMaxLength(DocumentUnderstandingLimits.MaximumDetectedTitleLength);

            understanding.Property(value => value.Subject)
                .HasMaxLength(DocumentUnderstandingLimits.MaximumSubjectLength);

            understanding.Property(value => value.Model)
                .HasMaxLength(DocumentUnderstandingLimits.MaximumModelLength);

            understanding.Property(value => value.PromptVersion)
                .HasMaxLength(DocumentUnderstandingLimits.MaximumPromptVersionLength);

            understanding.Property(value => value.SourceContentHash)
                .HasMaxLength(DocumentUnderstandingLimits.SourceContentHashLength);

            understanding.Property(value => value.LastError)
                .HasMaxLength(DocumentUnderstandingLimits.MaximumErrorLength);

            understanding.HasOne(value => value.Document)
                .WithOne(document => document.Understanding)
                .HasForeignKey<DocumentUnderstanding>(value => value.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DocumentMetadataEntry>(entry =>
        {
            entry.ToTable("DocumentMetadataEntries", table =>
            {
                table.HasCheckConstraint(
                    "CK_DocumentMetadataEntries_Confidence",
                    "\"Confidence\" IS NULL OR (\"Confidence\" >= 0 AND \"Confidence\" <= 1)");
            });

            entry.HasKey(value => value.Id);

            entry.Property(value => value.Kind)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            entry.Property(value => value.Label)
                .HasMaxLength(DocumentUnderstandingLimits.MaximumMetadataLabelLength)
                .IsRequired();

            entry.Property(value => value.Value)
                .HasMaxLength(DocumentUnderstandingLimits.MaximumMetadataValueLength)
                .IsRequired();

            entry.Property(value => value.NormalizedValue)
                .HasMaxLength(DocumentUnderstandingLimits.MaximumMetadataValueLength);

            entry.HasOne(value => value.DocumentUnderstanding)
                .WithMany(understanding => understanding.MetadataEntries)
                .HasForeignKey(value => value.DocumentUnderstandingId)
                .OnDelete(DeleteBehavior.Cascade);

            entry.HasIndex(value => new
                { value.DocumentUnderstandingId, value.Sequence })
                .IsUnique();

            entry.HasIndex(value => new { value.Kind, value.Label });
        });

        builder.Entity<DocumentTechnicalAnalysis>(analysis =>
        {
            analysis.ToTable("DocumentTechnicalAnalyses");

            analysis.HasKey(value => value.DocumentId);

            analysis.Property(value => value.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            analysis.Property(value => value.TechnicalType)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            analysis.Property(value => value.SourceFileHash)
                .HasMaxLength(PdfTechnicalAnalysisArchitecture.SourceFileHashLength);

            analysis.Property(value => value.AnalyzerVersion)
                .HasMaxLength(
                    PdfTechnicalAnalysisArchitecture.MaximumAnalyzerVersionLength);

            analysis.Property(value => value.LastError)
                .HasMaxLength(PdfTechnicalAnalysisArchitecture.MaximumErrorLength);

            analysis.HasOne(value => value.Document)
                .WithOne(document => document.TechnicalAnalysis)
                .HasForeignKey<DocumentTechnicalAnalysis>(value => value.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DocumentPageTechnicalAnalysis>(page =>
        {
            page.ToTable("DocumentPageTechnicalAnalyses", table =>
            {
                table.HasCheckConstraint(
                    "CK_DocumentPageTechnicalAnalyses_ImageCoverageRatio",
                    "\"ImageCoverageRatio\" >= 0 AND \"ImageCoverageRatio\" <= 1");
            });

            page.HasKey(value => new
                { value.DocumentTechnicalAnalysisId, value.PageNumber });

            page.Property(value => value.TechnicalType)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            page.HasOne(value => value.DocumentTechnicalAnalysis)
                .WithMany(analysis => analysis.Pages)
                .HasForeignKey(value => value.DocumentTechnicalAnalysisId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DocumentOcrAnalysis>(analysis =>
        {
            analysis.ToTable("DocumentOcrAnalyses");

            analysis.HasKey(value => value.DocumentId);

            analysis.Property(value => value.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            analysis.Property(value => value.EngineName)
                .HasMaxLength(OcrArchitecture.MaximumEngineNameLength);

            analysis.Property(value => value.EngineVersion)
                .HasMaxLength(OcrArchitecture.MaximumEngineVersionLength);

            analysis.Property(value => value.Languages)
                .HasMaxLength(OcrArchitecture.MaximumLanguagesLength);

            analysis.Property(value => value.SourceFileHash)
                .HasMaxLength(OcrArchitecture.HashLength);

            analysis.Property(value => value.RoutingVersion)
                .HasMaxLength(OcrArchitecture.MaximumRoutingVersionLength);

            analysis.Property(value => value.RoutingHash)
                .HasMaxLength(OcrArchitecture.HashLength);

            analysis.Property(value => value.ConfigurationHash)
                .HasMaxLength(OcrArchitecture.HashLength);

            analysis.Property(value => value.LastError)
                .HasMaxLength(OcrArchitecture.MaximumErrorLength);

            analysis.HasOne(value => value.Document)
                .WithOne(document => document.OcrAnalysis)
                .HasForeignKey<DocumentOcrAnalysis>(value => value.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DocumentPageOcrResult>(page =>
        {
            page.ToTable("DocumentPageOcrResults", table =>
            {
                table.HasCheckConstraint(
                    "CK_DocumentPageOcrResults_MeanConfidence",
                    "\"MeanConfidence\" IS NULL OR (\"MeanConfidence\" >= 0 AND \"MeanConfidence\" <= 1)");
            });

            page.HasKey(value => new
                { value.DocumentOcrAnalysisId, value.PageNumber });

            page.Property(value => value.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            page.Property(value => value.SourceTechnicalType)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            page.Property(value => value.LastError)
                .HasMaxLength(OcrArchitecture.MaximumErrorLength);

            page.HasOne(value => value.DocumentOcrAnalysis)
                .WithMany(analysis => analysis.Pages)
                .HasForeignKey(value => value.DocumentOcrAnalysisId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Conversation>(conversation =>
        {
            conversation.ToTable("Conversations");
            conversation.HasKey(value => value.Id);
            conversation.Property(value => value.Title)
                .HasMaxLength(ConversationLimits.MaximumTitleLength)
                .IsRequired();
            conversation.Property(value => value.CreatedAtUtc).IsRequired();
            conversation.Property(value => value.UpdatedAtUtc).IsRequired();
            conversation.HasOne(value => value.Project)
                .WithMany(project => project.Conversations)
                .HasForeignKey(value => value.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            conversation.HasIndex(value => new { value.ProjectId, value.UpdatedAtUtc });
        });

        builder.Entity<ConversationMessage>(message =>
        {
            message.ToTable("ConversationMessages");
            message.HasKey(value => value.Id);
            message.Property(value => value.Role)
                .HasConversion<string>()
                .HasMaxLength(16)
                .IsRequired();
            message.Property(value => value.Content)
                .HasColumnType("text")
                .IsRequired();
            message.Property(value => value.CreatedAtUtc).IsRequired();
            message.HasOne(value => value.Conversation)
                .WithMany(conversation => conversation.Messages)
                .HasForeignKey(value => value.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            message.HasIndex(value => new { value.ConversationId, value.Sequence })
                .IsUnique();
        });

        builder.Entity<ConversationMessageSource>(source =>
        {
            source.ToTable("ConversationMessageSources");
            source.HasKey(value => value.Id);
            source.Property(value => value.SourceId)
                .HasMaxLength(ConversationLimits.MaximumSourceIdLength)
                .IsRequired();
            source.Property(value => value.DocumentName)
                .HasMaxLength(ConversationLimits.MaximumDocumentNameLength)
                .IsRequired();
            source.Property(value => value.Heading)
                .HasMaxLength(ConversationLimits.MaximumHeadingLength);
            source.Property(value => value.Excerpt)
                .HasMaxLength(ConversationLimits.MaximumSourceExcerptLength)
                .IsRequired();
            source.HasOne(value => value.ConversationMessage)
                .WithMany(message => message.Sources)
                .HasForeignKey(value => value.ConversationMessageId)
                .OnDelete(DeleteBehavior.Cascade);
            source.HasIndex(value => new
                { value.ConversationMessageId, value.SourceIndex })
                .IsUnique();
        });
    }

    private static byte[]? SerializeEmbeddingForInMemory(Pgvector.Vector? embedding)
    {
        if (embedding is null)
        {
            return null;
        }

        var values = embedding.ToArray();
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static Pgvector.Vector? DeserializeEmbeddingForInMemory(byte[]? bytes)
    {
        if (bytes is null)
        {
            return null;
        }

        var values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return new Pgvector.Vector(values);
    }
}
