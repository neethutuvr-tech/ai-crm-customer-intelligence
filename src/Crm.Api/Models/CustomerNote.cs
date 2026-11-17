namespace Crm.Api.Models;

public sealed class CustomerNote : TenantEntity
{
    public Guid CustomerId { get; set; }
    public required string Body { get; set; }
    public Guid? AuthorUserId { get; set; }
    public Customer Customer { get; set; } = null!;
}
