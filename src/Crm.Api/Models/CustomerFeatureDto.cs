using System.Text.Json.Serialization;

namespace Crm.Api.Models;

public sealed record CustomerFeatureDto(
    [property: JsonPropertyName("customer_id")] string CustomerId,
    [property: JsonPropertyName("recency_days")] int RecencyDays,
    [property: JsonPropertyName("orders_365d")] int Orders365d,
    [property: JsonPropertyName("spend_365d")] double Spend365d,
    [property: JsonPropertyName("avg_order_value")] double AverageOrderValue,
    [property: JsonPropertyName("sessions_30d")] int Sessions30d,
    [property: JsonPropertyName("product_views_30d")] int ProductViews30d,
    [property: JsonPropertyName("cart_adds_30d")] int CartAdds30d,
    [property: JsonPropertyName("campaign_click_rate")] double CampaignClickRate,
    [property: JsonPropertyName("support_cases_90d")] int SupportCases90d,
    [property: JsonPropertyName("enquiries_90d")] int Enquiries90d,
    [property: JsonPropertyName("tenure_days")] int TenureDays);

public sealed record CustomerAssistantContext(
    [property: JsonPropertyName("crm_status")] string CrmStatus,
    [property: JsonPropertyName("signup_date")] DateOnly? SignupDate,
    [property: JsonPropertyName("recency_days")] int RecencyDays,
    [property: JsonPropertyName("orders_365d")] int Orders365d,
    [property: JsonPropertyName("spend_365d")] double Spend365d,
    [property: JsonPropertyName("avg_order_value")] double AverageOrderValue,
    [property: JsonPropertyName("sessions_30d")] int Sessions30d,
    [property: JsonPropertyName("product_views_30d")] int ProductViews30d,
    [property: JsonPropertyName("cart_adds_30d")] int CartAdds30d,
    [property: JsonPropertyName("campaign_click_rate")] double CampaignClickRate,
    [property: JsonPropertyName("support_cases_90d")] int SupportCases90d,
    [property: JsonPropertyName("enquiries_90d")] int Enquiries90d,
    [property: JsonPropertyName("open_followups")] int OpenFollowUps,
    [property: JsonPropertyName("has_validated_churn_model")] bool HasValidatedChurnModel = false,
    [property: JsonPropertyName("has_validated_purchase_model")] bool HasValidatedPurchaseModel = false);
