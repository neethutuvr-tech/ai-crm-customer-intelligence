using Crm.Api.Data;
using Crm.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crm.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Manager")]
public sealed class InsightsController(
    CrmDbContext database,
    CustomerIntelligenceProfileService profileService,
    IPredictionService predictionService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetInsights(CancellationToken cancellationToken = default)
    {
        var activeCustomers = database.Customers.AsNoTracking().Where(row => row.Status != "Archived");
        var customerCount = await activeCustomers.CountAsync(cancellationToken);
        var syntheticCount = await activeCustomers.CountAsync(row => row.IsSynthetic, cancellationToken);
        var outcomes = database.CustomerOutcomes.AsNoTracking();
        var labelCohorts = await outcomes
            .Select(row => new { row.Customer.IsSynthetic, row.ChurnNext30d, row.PurchaseNext30d })
            .GroupBy(row => row.IsSynthetic)
            .Select(group => new
            {
                IsSynthetic = group.Key,
                SnapshotCount = group.Count(),
                ChurnPositive = group.Count(row => row.ChurnNext30d),
                RepeatPurchasePositive = group.Count(row => row.PurchaseNext30d)
            })
            .ToListAsync(cancellationToken);
        var realLabels = labelCohorts.FirstOrDefault(row => !row.IsSynthetic);
        var syntheticLabels = labelCohorts.FirstOrDefault(row => row.IsSynthetic);

        return Ok(new
        {
            dataSource = "tenant_database",
            asOfUtc = DateTime.UtcNow,
            customerCount,
            realCustomerCount = customerCount - syntheticCount,
            syntheticCustomerCount = syntheticCount,
            transactionCount = await database.Transactions.CountAsync(cancellationToken),
            interactionCount = await database.Interactions.CountAsync(cancellationToken),
            openFollowUpCount = await database.FollowUps.CountAsync(row => row.CompletedAtUtc == null, cancellationToken),
            labels = new
            {
                real = new
                {
                    customerOutcomeSnapshots = realLabels?.SnapshotCount ?? 0,
                    churnPositive = realLabels?.ChurnPositive ?? 0,
                    repeatPurchasePositive = realLabels?.RepeatPurchasePositive ?? 0
                },
                synthetic = new
                {
                    customerOutcomeSnapshots = syntheticLabels?.SnapshotCount ?? 0,
                    churnPositive = syntheticLabels?.ChurnPositive ?? 0,
                    repeatPurchasePositive = syntheticLabels?.RepeatPurchasePositive ?? 0
                }
            },
            churnModel = new
            {
                status = "not_evaluated",
                reason = "This API does not show churn probabilities until aligned historical feature snapshots have been validated with temporal evaluation."
            },
            repeatPurchaseModel = new
            {
                status = "not_evaluated",
                reason = "This API does not show repeat-purchase probabilities until aligned historical feature snapshots have been validated with temporal evaluation."
            }
        });
    }

    [HttpGet("segmentation")]
    public async Task<IActionResult> GetSegmentation(
        [FromQuery] int clusters = 4,
        [FromQuery] bool includeSynthetic = false,
        CancellationToken cancellationToken = default)
    {
        if (clusters is < 2 or > 8)
            return BadRequest(new { error = "clusters must be between 2 and 8." });

        var eligibleCustomers = database.Customers.AsNoTracking().Where(row => row.Status != "Archived");
        if (!includeSynthetic)
            eligibleCustomers = eligibleCustomers.Where(row => !row.IsSynthetic);
        var eligibleCount = await eligibleCustomers.CountAsync(cancellationToken);
        if (eligibleCount > 10_000)
            return BadRequest(new { error = "This first implementation accepts at most 10,000 customers per segmentation run." });
        if (eligibleCount < clusters + 1)
            return UnprocessableEntity(new
            {
                error = $"At least {clusters + 1} customers are required for a {clusters}-cluster analysis.",
                customerCount = eligibleCount
            });

        var profiles = await profileService.GetProfilesAsync(includeSynthetic, ownerUserId: null, cancellationToken);

        var result = await predictionService.GetSegmentationAsync(
            profiles.Select(row => row.Features).ToArray(), clusters, cancellationToken);
        return Ok(new
        {
            dataSource = "tenant_database",
            syntheticRecordsIncluded = includeSynthetic,
            syntheticRecordCount = profiles.Count(row => row.IsSynthetic),
            realRecordCount = profiles.Count(row => !row.IsSynthetic),
            result
        });
    }

    [HttpGet("follow-ups")]
    public async Task<IActionResult> GetFollowUps(
        [FromQuery] int daysAhead = 7,
        CancellationToken cancellationToken = default)
    {
        if (daysAhead is < 0 or > 90)
            return BadRequest(new { error = "daysAhead must be between 0 and 90." });

        var dueThrough = DateTime.UtcNow.AddDays(daysAhead);
        var followUps = await database.FollowUps.AsNoTracking()
            .Where(row => row.CompletedAtUtc == null && row.DueAtUtc <= dueThrough)
            .OrderBy(row => row.DueAtUtc)
            .Select(row => new
            {
                row.Id,
                customerId = row.CustomerId,
                customerName = row.Customer.Name,
                row.Subject,
                row.DueAtUtc,
                row.OwnerUserId
            })
            .ToListAsync(cancellationToken);
        return Ok(followUps);
    }
}
