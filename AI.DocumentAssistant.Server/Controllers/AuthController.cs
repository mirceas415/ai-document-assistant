using AI.DocumentAssistant.Server.Contracts;
using AI.DocumentAssistant.Server.Models;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AI.DocumentAssistant.Server.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<UserResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var email = NormalizeEmail(request.Email);
        var displayName = NormalizeDisplayName(request.DisplayName);

        if (!new EmailAddressAttribute().IsValid(email))
        {
            return ValidationError("email", "Enter a valid email address.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return ValidationError("displayName", "Display name is required.");
        }

        if (displayName.Length > 100)
        {
            return ValidationError("displayName", "Display name cannot exceed 100 characters.");
        }

        if (await _userManager.FindByEmailAsync(email) is not null)
        {
            return Conflict(new ApiErrorResponse(
                "An account with this email address already exists."));
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            DisplayName = displayName,
            CreatedAtUtc = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            return IdentityValidationError(result.Errors);
        }

        await _signInManager.SignInAsync(user, isPersistent: false);

        return Ok(ToResponse(user));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<UserResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var email = NormalizeEmail(request.Email);

        if (!new EmailAddressAttribute().IsValid(email))
        {
            return ValidationError("email", "Enter a valid email address.");
        }

        var result = await _signInManager.PasswordSignInAsync(
            email,
            request.Password,
            request.RememberMe,
            lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            return Unauthorized(new ApiErrorResponse(
                "Invalid email or password, or the account is temporarily locked."));
        }

        var user = await _userManager.FindByNameAsync(email);

        if (user is null)
        {
            await _signInManager.SignOutAsync();
            return Unauthorized(new ApiErrorResponse(
                "Invalid email or password, or the account is temporarily locked."));
        }

        return Ok(ToResponse(user));
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _signInManager.SignOutAsync();
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _userManager.GetUserAsync(User);

        return user is null
            ? Unauthorized(new ApiErrorResponse("Authentication is required."))
            : Ok(ToResponse(user));
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    private static string NormalizeDisplayName(string displayName) =>
        string.Join(' ', displayName.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private ActionResult ValidationError(string field, string message) =>
        BadRequest(new ApiErrorResponse(
            "Validation failed.",
            new Dictionary<string, string[]> { [field] = [message] }));

    private ActionResult IdentityValidationError(IEnumerable<IdentityError> errors)
    {
        var groupedErrors = errors
            .GroupBy(error => GetErrorField(error.Code))
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Description).ToArray());

        return BadRequest(new ApiErrorResponse("Registration failed.", groupedErrors));
    }

    private static string GetErrorField(string code) =>
        code.Contains("Password", StringComparison.OrdinalIgnoreCase)
            ? "password"
            : code.Contains("Email", StringComparison.OrdinalIgnoreCase) ||
              code.Contains("UserName", StringComparison.OrdinalIgnoreCase)
                ? "email"
                : "request";

    private static UserResponse ToResponse(ApplicationUser user) =>
        new(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            user.CreatedAtUtc);
}
