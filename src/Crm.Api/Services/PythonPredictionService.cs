using System.Net.Http.Json;
using System.Text.Json;
using Crm.Api.Models;

namespace Crm.Api.Services;

public sealed class PythonPredictionService(HttpClient httpClient) : IPredictionService
{
    public async Task<IReadOnlyList<ScoredCustomerDto>> GetCustomersAsync(
        int clusters,
        CancellationToken cancellationToken)
    {
        return await httpClient.GetFromJsonAsync<List<ScoredCustomerDto>>(
                   $"customers?clusters={clusters}", cancellationToken)
               ?? [];
    }

    public async Task<JsonElement> GetMetricsAsync(int clusters, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"metrics?clusters={clusters}", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    public async Task<JsonElement> GetRecommendationAsync(
        AssistantRequestDto request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "assistant/recommendations",
            new { customer_id = request.CustomerId, question = request.Question, clusters = request.Clusters },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }
}

