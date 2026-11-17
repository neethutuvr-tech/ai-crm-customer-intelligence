using Crm.Api.Models;
using Crm.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AssistantController(IPredictionService predictionService) : ControllerBase
{
    [HttpPost("recommendations")]
    public async Task<IActionResult> GetRecommendation(
        [FromBody] AssistantRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerId)
            || string.IsNullOrWhiteSpace(request.Question)
            || request.Clusters is < 2 or > 8)
        {
            return BadRequest("customerId, question and a cluster count from 2 to 8 are required.");
        }

        return Ok(await predictionService.GetRecommendationAsync(request, cancellationToken));
    }
}

