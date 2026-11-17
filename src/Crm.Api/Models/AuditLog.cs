namespace Crm.Api.Models;

public sealed class AuditLog : TenantEntity
{
    public Guid? ActorUserId { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? DetailsJson { get; set; }
}
