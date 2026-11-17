namespace Crm.Api.Models;

public sealed class CustomerFollowUp : TenantEntity
{
    public Guid CustomerId { get; set; }
    public required string Subject { get; set; }
    public DateTime DueAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public Guid? OwnerUserId { get; set; }
    public Customer Customer { get; set; } = null!;
}
