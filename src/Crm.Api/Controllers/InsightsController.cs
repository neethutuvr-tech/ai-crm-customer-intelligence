using Crm.Api.Models;
using Crm.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class InsightsController(
    IPredictionService predictionService,
    ICustomerRepository customerRepository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetInsights(
        [FromQuery] int clusters = 4,
        CancellationToken cancellationToken = default)
    {
        if (clusters is < 2 or > 8)
        {
            return BadRequest("clusters must be between 2 and 8.");
        }
        return Ok(await predictionService.GetMetricsAsync(clusters, cancellationToken));
    }

    [HttpGet("follow-ups")]
    public async Task<ActionResult<IReadOnlyList<FollowUpDto>>> GetFollowUps(
        [FromQuery] int clusters = 4,
        [FromQuery] double threshold = 0.65,
        CancellationToken cancellationToken = default)
    {
        if (clusters is < 2 or > 8 || threshold is < 0 or > 1)
        {
            return BadRequest("clusters must be 2–8 and threshold must be between 0 and 1.");
        }

        var customers = await predictionService.GetCustomersAsync(clusters, cancellationToken);
        var profiles = await customerRepository.GetAllAsync(cancellationToken);
        var profileById = profiles.ToDictionary(row => row.CustomerId, StringComparer.OrdinalIgnoreCase);
        var queue = customers.Select(row => new FollowUpDto(
                row.CustomerId,
                profileById.TryGetValue(row.CustomerId, out var profile) ? profile.City : "",
                row.Segment,
                row.ChurnProbability,
                row.PurchaseProbability,
                row.ChurnProbability >= threshold
                    ? "Retention review"
                    : row.PurchaseProbability >= threshold
                        ? "Personalized offer review"
                        : "Monitor"))
            .Where(row => row.SuggestedReview != "Monitor")
            .OrderByDescending(row => Math.Max(row.ChurnProbability, row.PurchaseProbability))
            .ToList();
        return Ok(queue);
    }
}

