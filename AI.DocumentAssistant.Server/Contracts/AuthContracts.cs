using System.ComponentModel.DataAnnotations;

namespace AI.DocumentAssistant.Server.Contracts;

public sealed class RegisterRequest
{
    [Required]
    [StringLength(320)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string Password { get; init; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string DisplayName { get; init; } = string.Empty;
}

public sealed class LoginRequest
{
    [Required]
    [StringLength(320)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [StringLength(128)]
    public string Password { get; init; } = string.Empty;

    public bool RememberMe { get; init; }
}

public sealed record UserResponse(
    Guid Id,
    string Email,
    string DisplayName,
    DateTime CreatedAtUtc);

public sealed record ApiErrorResponse(
    string Message,
    IReadOnlyDictionary<string, string[]>? Errors = null);
