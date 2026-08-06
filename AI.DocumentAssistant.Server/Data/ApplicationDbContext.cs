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
    }
}
