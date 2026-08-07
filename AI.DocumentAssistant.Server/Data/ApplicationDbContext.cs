using AI.DocumentAssistant.Server.Embeddings;
using AI.DocumentAssistant.Server.Models;
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
