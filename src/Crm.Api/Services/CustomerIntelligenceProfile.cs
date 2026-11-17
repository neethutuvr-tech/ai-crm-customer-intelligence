using Crm.Api.Models;

namespace Crm.Api.Services;

public sealed record CustomerIntelligenceProfile(
    Guid CustomerId,
    string Status,
    DateOnly? SignupDate,
    bool IsSynthetic,
    CustomerFeatureDto Features,
    int OpenFollowUps)
{
    public CustomerAssistantContext ToAssistantContext() => new(
        Status,
        SignupDate,
        Features.RecencyDays,
        Features.Orders365d,
        Features.Spend365d,
        Features.AverageOrderValue,
        Features.Sessions30d,
        Features.ProductViews30d,
        Features.CartAdds30d,
        Features.CampaignClickRate,
        Features.SupportCases90d,
        Features.Enquiries90d,
        OpenFollowUps);
}
