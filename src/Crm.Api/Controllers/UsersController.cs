using Crm.Api.Data;
using Crm.Api.Models;
using Crm.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crm.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = "Admin")]
public sealed class UsersController(
    CrmDbContext database,
    UserManager<ApplicationUser> userManager,
    ITenantContext tenant,
    AuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        var organizationId = tenant.RequireOrganizationId();
        var users = await database.Users.AsNoTracking()
            .Where(row => row.OrganizationId == organizationId)
            .OrderBy(row => row.DisplayName)
            .ToListAsync(cancellationToken);
        var response = new List<object>(users.Count);
        foreach (var user in users)
        {
            response.Add(new
            {
                user.Id,
                user.Email,
                user.DisplayName,
                user.IsActive,
                user.CreatedAtUtc,
                roles = await userManager.GetRolesAsync(user)
            });
        }
        return Ok(response);
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser(CreateUserRequest request, CancellationToken cancellationToken)
    {
        if (request.Role is not (Roles.Manager or Roles.SalesExecutive))
            return BadRequest(new { error = "An organization administrator can assign Manager or Sales Executive." });

        var email = request.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
            return Conflict(new { error = "An account with this email already exists." });

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            OrganizationId = tenant.RequireOrganizationId(),
            IsActive = true
        };
        var result = await userManager.CreateAsync(user, request.TemporaryPassword);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(error => error.Description) });
        var assigned = await userManager.AddToRoleAsync(user, request.Role);
        if (!assigned.Succeeded)
        {
            await userManager.DeleteAsync(user);
            return Problem("The requested role could not be assigned.", statusCode: 500);
        }

        await audit.RecordAsync(user.OrganizationId, "user.created", "User", user.Id.ToString(),
            new { role = request.Role }, cancellationToken);
        return Created("/api/users", new { user.Id, user.Email, user.DisplayName, role = request.Role });
    }
}
