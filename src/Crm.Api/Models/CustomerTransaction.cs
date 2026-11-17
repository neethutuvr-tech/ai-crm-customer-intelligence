namespace Crm.Api.Models;

public sealed class CustomerTransaction : TenantEntity
{
    public Guid CustomerId { get; set; }
    public required string ExternalTransactionId { get; set; }
    public DateOnly OrderDate { get; set; }
    public decimal Amount { get; set; }
    public string? Category { get; set; }
    public string? Channel { get; set; }
    public bool IsSynthetic { get; set; }
    public Customer Customer { get; set; } = null!;
}
