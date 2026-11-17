namespace Crm.Api.Models;

public sealed class CustomerActivitySnapshot : TenantEntity
{
    public Guid CustomerId { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public int Sessions30d { get; set; }
    public int ProductViews30d { get; set; }
    public int CartAdds30d { get; set; }
    public int CampaignEmails30d { get; set; }
    public int CampaignClicks30d { get; set; }
    public Customer Customer { get; set; } = null!;
}
