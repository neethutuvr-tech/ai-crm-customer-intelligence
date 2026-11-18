using System.Net.Http.Json;
using System.Text.Json;
using Crm.Api.Models;

namespace Crm.Api.Services;

public sealed class PythonPredictionService(HttpClient httpClient) : IPredictionService
{
    public async Task<JsonElement> GetSegmentationAsync(
        IReadOnlyList<CustomerFeatureDto> customers,
        int clusters,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "segmentation",
            new { customers, clusters },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    public async Task<JsonElement> GetSegmentationResearchAsync(
        IReadOnlyList<CustomerFeatureDto> customers,
        int clusters,
        double dbscanEps,
        int dbscanMinSamples,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "segmentation/research",
            new { customers, clusters, dbscan_eps = dbscanEps, dbscan_min_samples = dbscanMinSamples },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    public async Task<JsonElement> GetKnowledgeStatusAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("knowledge/status", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    public async Task<JsonElement> ReindexKnowledgeAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync("knowledge/reindex", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    public async Task<JsonElement> GetRecommendationAsync(
        string question,
        CustomerAssistantContext customerContext,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "assistant/tenant-recommendations",
            new { question, customer_context = customerContext },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }
}

