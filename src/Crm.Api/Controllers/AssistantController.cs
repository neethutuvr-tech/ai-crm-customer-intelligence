using Crm.Api.Models;
using Crm.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager,Sales Executive")]
public sealed class AssistantController(
    IPredictionService predictionService,
    CustomerIntelligenceProfileService profileService,
    ITenantContext tenant) : ControllerBase
{
    [HttpPost("recommendations")]
    public async Task<IActionResult> GetRecommendation(
        [FromBody] AssistantRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(request.CustomerId)
            || !Guid.TryParse(request.CustomerId, out var customerId)
            || string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new { error = "A valid customerId GUID and question are required." });
        }

        Guid? ownerId = User.IsInRole(Roles.SalesExecutive) ? tenant.RequireUserId() : null;
        var profile = await profileService.GetProfileAsync(customerId, ownerId, cancellationToken);
        if (profile is null)
            return NotFound();

        var answer = await predictionService.GetRecommendationAsync(
            request.Question.Trim(), profile.ToAssistantContext(), cancellationToken);
        return Ok(answer);
    }
}
