namespace Crm.Api.Services;

public interface ITenantContext
{
    Guid? OrganizationId { get; }
    Guid? UserId { get; }
    string? Role { get; }
    Guid RequireOrganizationId();
    Guid RequireUserId();
}
