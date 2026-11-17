namespace Crm.Api.Models;

public sealed class Lead : TenantEntity
{
    public required string Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Company { get; set; }
    public string? Source { get; set; }
    public string Status { get; set; } = "New";
    public Guid? OwnerUserId { get; set; }
    public Guid? ConvertedCustomerId { get; set; }
}
