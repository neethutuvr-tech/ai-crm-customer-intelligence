using System.Text.Json;
using Crm.Api.Models;

namespace Crm.Api.Services;

public interface IPredictionService
{
    Task<JsonElement> GetSegmentationAsync(
        IReadOnlyList<CustomerFeatureDto> customers,
        int clusters,
        CancellationToken cancellationToken);

    Task<JsonElement> GetSegmentationResearchAsync(
        IReadOnlyList<CustomerFeatureDto> customers,
        int clusters,
        double dbscanEps,
        int dbscanMinSamples,
        CancellationToken cancellationToken);

    Task<JsonElement> GetRecommendationAsync(
        string question,
        CustomerAssistantContext customerContext,
        CancellationToken cancellationToken);
}

