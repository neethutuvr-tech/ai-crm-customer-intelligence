using System.Text.Json;
using Crm.Api.Models;

namespace Crm.Api.Services;

public interface IPredictionService
{
    Task<IReadOnlyList<ScoredCustomerDto>> GetCustomersAsync(int clusters, CancellationToken cancellationToken);
    Task<JsonElement> GetMetricsAsync(int clusters, CancellationToken cancellationToken);
    Task<JsonElement> GetRecommendationAsync(AssistantRequestDto request, CancellationToken cancellationToken);
}

