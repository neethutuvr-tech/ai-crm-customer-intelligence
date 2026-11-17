namespace Crm.Api.Models;

public sealed record FollowUpDto(
    string CustomerId,
    string City,
    string Segment,
    double ChurnProbability,
    double PurchaseProbability,
    string SuggestedReview);

