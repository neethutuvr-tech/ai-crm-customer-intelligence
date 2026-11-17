namespace Crm.Api.Models;

public sealed class CustomerInteraction : TenantEntity
{
    public Guid CustomerId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public required string Type { get; set; }
    public string? Summary { get; set; }
    public string? Outcome { get; set; }
    public Guid? RecordedByUserId { get; set; }
    public bool IsSynthetic { get; set; }
    public Customer Customer { get; set; } = null!;
}
