using System.Security.Claims;
using System.Text.Json;
using Crm.Api.Data;
using Crm.Api.Models;

namespace Crm.Api.Services;

public sealed class AuditService(CrmDbContext database, IHttpContextAccessor httpContextAccessor)
{
    public async Task RecordAsync(
        Guid organizationId,
        string action,
        string entityType,
        string? entityId = null,
        object? details = null,
        CancellationToken cancellationToken = default)
    {
        var actor = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        database.AuditLogs.Add(new AuditLog
        {
            OrganizationId = organizationId,
            ActorUserId = Guid.TryParse(actor, out var actorId) ? actorId : null,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details)
        });
        await database.SaveChangesAsync(cancellationToken);
    }
}
