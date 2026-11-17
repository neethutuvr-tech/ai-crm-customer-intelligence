using System.Text.Json.Serialization;

namespace Crm.Api.Models;

public sealed class ScoredCustomerDto
{
    [JsonPropertyName("customer_id")]
    public string CustomerId { get; init; } = "";

    [JsonPropertyName("segment")]
    public string Segment { get; init; } = "";

    [JsonPropertyName("recency_days")]
    public int RecencyDays { get; init; }

    [JsonPropertyName("orders_365d")]
    public int Orders365d { get; init; }

    [JsonPropertyName("spend_365d")]
    public double Spend365d { get; init; }

    [JsonPropertyName("avg_order_value")]
    public double AverageOrderValue { get; init; }

    [JsonPropertyName("campaign_click_rate")]
    public double CampaignClickRate { get; init; }

    [JsonPropertyName("churn_probability")]
    public double ChurnProbability { get; init; }

    [JsonPropertyName("purchase_probability")]
    public double PurchaseProbability { get; init; }

    [JsonPropertyName("priority_score")]
    public double PriorityScore { get; init; }
}

