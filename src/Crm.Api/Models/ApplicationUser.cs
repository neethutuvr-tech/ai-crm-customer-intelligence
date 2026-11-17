using Microsoft.AspNetCore.Identity;

namespace Crm.Api.Models;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public Guid OrganizationId { get; set; }
    public required string DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
