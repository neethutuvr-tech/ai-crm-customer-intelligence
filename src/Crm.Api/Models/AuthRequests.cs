using System.ComponentModel.DataAnnotations;

namespace Crm.Api.Models;

public sealed class RegisterRequest
{
    [Required, StringLength(160, MinimumLength = 2)]
    public string OrganizationName { get; init; } = "";
    [Required, StringLength(120, MinimumLength = 2)]
    public string DisplayName { get; init; } = "";
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = "";
    [Required, MinLength(12)]
    public string Password { get; init; } = "";
}

public sealed class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; init; } = "";
    [Required]
    public string Password { get; init; } = "";
}

public sealed class PasswordResetRequest
{
    [Required, EmailAddress]
    public string Email { get; init; } = "";
}

public sealed class PasswordResetCompleteRequest
{
    [Required, EmailAddress]
    public string Email { get; init; } = "";
    [Required]
    public string Token { get; init; } = "";
    [Required, MinLength(12)]
    public string NewPassword { get; init; } = "";
}

public sealed class CreateUserRequest
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string DisplayName { get; init; } = "";
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = "";
    [Required, MinLength(12)]
    public string TemporaryPassword { get; init; } = "";
    [Required]
    public string Role { get; init; } = "";
}
