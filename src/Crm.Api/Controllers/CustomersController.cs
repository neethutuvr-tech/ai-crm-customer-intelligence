using Crm.Api.Models;
using Crm.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class CustomersController(
    ICustomerRepository customerRepository,
    IPredictionService predictionService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerViewDto>>> GetCustomers(
        [FromQuery] int clusters = 4,
        [FromQuery] string? segment = null,
        [FromQuery] double? minChurnRisk = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (clusters is < 2 or > 8 || minChurnRisk is < 0 or > 1)
        {
            return BadRequest("clusters must be 2–8 and minChurnRisk must be between 0 and 1.");
        }

        var profiles = await customerRepository.GetAllAsync(cancellationToken);
        var profileById = profiles.ToDictionary(row => row.CustomerId, StringComparer.OrdinalIgnoreCase);
        var scores = await predictionService.GetCustomersAsync(clusters, cancellationToken);
        IEnumerable<CustomerViewDto> rows = scores
            .Where(score => profileById.ContainsKey(score.CustomerId))
            .Select(score =>
            {
                var profile = profileById[score.CustomerId];
                return new CustomerViewDto(
                    score.CustomerId,
                    profile.City,
                    profile.AcquisitionChannel,
                    profile.SignupDate,
                    score.Segment,
                    score.RecencyDays,
                    score.Orders365d,
                    score.Spend365d,
                    score.AverageOrderValue,
                    score.CampaignClickRate,
                    score.ChurnProbability,
                    score.PurchaseProbability,
                    score.PriorityScore);
            });

        if (!string.IsNullOrWhiteSpace(segment))
        {
            rows = rows.Where(row => row.Segment.Contains(segment, StringComparison.OrdinalIgnoreCase));
        }
        if (minChurnRisk.HasValue)
        {
            rows = rows.Where(row => row.ChurnProbability >= minChurnRisk.Value);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            rows = rows.Where(row => row.CustomerId.Contains(search, StringComparison.OrdinalIgnoreCase)
                                     || row.City.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        return Ok(rows.OrderByDescending(row => row.PriorityScore).ToList());
    }

    [HttpGet("{customerId}")]
    public async Task<ActionResult<CustomerViewDto>> GetCustomer(
        string customerId,
        [FromQuery] int clusters = 4,
        CancellationToken cancellationToken = default)
    {
        if (clusters is < 2 or > 8)
        {
            return BadRequest("clusters must be between 2 and 8.");
        }

        var profiles = await customerRepository.GetAllAsync(cancellationToken);
        var profile = profiles.FirstOrDefault(row =>
            row.CustomerId.Equals(customerId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            return NotFound();
        }

        var scores = await predictionService.GetCustomersAsync(clusters, cancellationToken);
        var score = scores.FirstOrDefault(row =>
            row.CustomerId.Equals(customerId, StringComparison.OrdinalIgnoreCase));
        if (score is null)
        {
            return NotFound();
        }

        return Ok(new CustomerViewDto(
            score.CustomerId,
            profile.City,
            profile.AcquisitionChannel,
            profile.SignupDate,
            score.Segment,
            score.RecencyDays,
            score.Orders365d,
            score.Spend365d,
            score.AverageOrderValue,
            score.CampaignClickRate,
            score.ChurnProbability,
            score.PurchaseProbability,
            score.PriorityScore));
    }
}

