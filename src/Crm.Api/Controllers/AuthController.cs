using Crm.Api.Data;
using Crm.Api.Models;
using Crm.Api.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Crm.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    CrmDbContext database,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IAppEmailSender emailSender,
    AuditService audit,
    IWebHostEnvironment environment,
    IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("csrf")]
    [AllowAnonymous]
    public IActionResult GetCsrfToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = tokens.RequestToken });
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return Conflict(new { error = "An account with this email already exists." });
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var organization = new Organization { Name = request.OrganizationName.Trim() };
        database.Organizations.Add(organization);
        await database.SaveChangesAsync(cancellationToken);

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            OrganizationId = organization.Id,
            IsActive = true
        };
        var created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(new { errors = created.Errors.Select(error => error.Description) });
        }

        var assigned = await userManager.AddToRoleAsync(user, Roles.Admin);
        if (!assigned.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Problem("The initial organization administrator could not be assigned.", statusCode: 500);
        }

        await transaction.CommitAsync(cancellationToken);
        await signInManager.SignInAsync(user, isPersistent: false);
        await audit.RecordAsync(organization.Id, "organization.registered", "Organization", organization.Id.ToString(), cancellationToken: cancellationToken);
        return Created("/api/auth/me", new { userId = user.Id, user.Email, user.DisplayName, organizationId = organization.Id, organizationName = organization.Name, role = Roles.Admin });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive)
        {
            return Unauthorized(new { error = "Email or password is invalid." });
        }

        var result = await signInManager.PasswordSignInAsync(
            user.UserName!, request.Password, isPersistent: false, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            return Unauthorized(new { error = "Email or password is invalid." });
        }

        var roles = await userManager.GetRolesAsync(user);
        await audit.RecordAsync(user.OrganizationId, "auth.login", "User", user.Id.ToString(), cancellationToken: cancellationToken);
        return Ok(new { user.Id, user.Email, user.DisplayName, user.OrganizationId, roles });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null || !user.IsActive)
        {
            await signInManager.SignOutAsync();
            return Unauthorized();
        }

        var organization = await database.Organizations
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == user.OrganizationId, cancellationToken);
        return Ok(new
        {
            user.Id,
            user.Email,
            user.DisplayName,
            user.OrganizationId,
            organizationName = organization?.Name,
            roles = await userManager.GetRolesAsync(user)
        });
    }

    [HttpPost("password-reset/request")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> RequestPasswordReset(PasswordResetRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is not null && user.IsActive)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            await emailSender.SendPasswordResetAsync(user.Email!, token, cancellationToken);
        }

        var response = new { message = "If the account exists, password reset instructions have been sent." };
        if (environment.IsDevelopment() && user is not null && user.IsActive)
        {
            return Ok(new { response.message, developmentToken = "See the local API log." });
        }
        return Ok(response);
    }

    [HttpPost("password-reset/complete")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> CompletePasswordReset(
        PasswordResetCompleteRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive)
        {
            return BadRequest(new { error = "The reset request is invalid or expired." });
        }

        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors.Select(error => error.Description) });
        }

        await audit.RecordAsync(user.OrganizationId, "auth.password_reset", "User", user.Id.ToString(), cancellationToken: cancellationToken);
        return Ok(new { message = "Password reset complete. Sign in with the new password." });
    }
}
