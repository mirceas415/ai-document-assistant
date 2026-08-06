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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

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
    }
}
