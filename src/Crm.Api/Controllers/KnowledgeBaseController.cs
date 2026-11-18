using Crm.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crm.Api.Controllers;

[ApiController]
[Route("api/knowledge")]
[Authorize(Roles = "Admin,Manager")]
public sealed class KnowledgeBaseController(IPredictionService predictionService) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken) =>
        Ok(await predictionService.GetKnowledgeStatusAsync(cancellationToken));

    [HttpPost("reindex")]
    public async Task<IActionResult> Reindex(CancellationToken cancellationToken) =>
        Ok(await predictionService.ReindexKnowledgeAsync(cancellationToken));
}
