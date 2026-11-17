using System.Security.Claims;

namespace Crm.Api.Services;

public sealed class HttpTenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public Guid? OrganizationId => Parse(User?.FindFirstValue("organization_id"));
    public Guid? UserId => Parse(User?.FindFirstValue(ClaimTypes.NameIdentifier));
    public string? Role => User?.FindFirstValue(ClaimTypes.Role);

    public Guid RequireOrganizationId() => OrganizationId
        ?? throw new InvalidOperationException("The signed-in user has no organization claim.");

    public Guid RequireUserId() => UserId
        ?? throw new InvalidOperationException("A signed-in user is required.");

    private static Guid? Parse(string? value) => Guid.TryParse(value, out var id) ? id : null;
}
