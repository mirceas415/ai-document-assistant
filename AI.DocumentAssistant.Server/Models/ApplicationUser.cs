using Microsoft.AspNetCore.Identity;

namespace AI.DocumentAssistant.Server.Models;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Project> OwnedProjects { get; } = new List<Project>();
}
