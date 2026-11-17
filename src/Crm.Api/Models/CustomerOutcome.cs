namespace Crm.Api.Models;

public sealed class CustomerOutcome : TenantEntity
{
    public Guid CustomerId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public bool ChurnNext30d { get; set; }
    public bool PurchaseNext30d { get; set; }
    public bool IsSynthetic { get; set; }
    public Customer Customer { get; set; } = null!;
}
