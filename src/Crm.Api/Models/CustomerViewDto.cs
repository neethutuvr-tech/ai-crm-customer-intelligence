namespace Crm.Api.Models;

public sealed record CustomerViewDto(
    string CustomerId,
    string City,
    string AcquisitionChannel,
    DateOnly SignupDate,
    string Segment,
    int RecencyDays,
    int Orders365d,
    double Spend365d,
    double AverageOrderValue,
    double CampaignClickRate,
    double ChurnProbability,
    double PurchaseProbability,
    double PriorityScore);

