using Crm.Api.Data;
using Crm.Api.Models;
using Crm.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crm.Api.Controllers;

[ApiController]
[Route("api/customers/{customerId:guid}")]
[Authorize(Roles = "Admin,Manager,Sales Executive")]
public sealed class CustomerActivityController(
    CrmDbContext database,
    ITenantContext tenant,
    AuditService audit) : ControllerBase
{
    [HttpGet("transactions")]
    public async Task<IActionResult> GetTransactions(
        Guid customerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            return BadRequest(new { error = "page must be positive and pageSize must be between 1 and 100." });
        if (!await CanAccessCustomerAsync(customerId, cancellationToken))
            return NotFound();

        var query = database.Transactions.AsNoTracking().Where(row => row.CustomerId == customerId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(row => row.OrderDate)
            .ThenByDescending(row => row.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(row => new { row.Id, row.ExternalTransactionId, row.OrderDate, row.Amount, row.Category, row.Channel, row.IsSynthetic })
            .ToListAsync(cancellationToken);
        return Ok(new { items, page, pageSize, total, pages = (int)Math.Ceiling(total / (double)pageSize) });
    }

    [HttpPost("transactions")]
    public async Task<IActionResult> CreateTransaction(
        Guid customerId,
        TransactionWriteRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);
        var customer = await FindAccessibleCustomerAsync(customerId, cancellationToken);
        if (customer is null)
            return NotFound();
        if (request.OrderDate == default)
            return BadRequest(new { error = "OrderDate is required." });
        var externalId = request.ExternalTransactionId.Trim();
        if (await database.Transactions.AnyAsync(row => row.ExternalTransactionId == externalId, cancellationToken))
            return Conflict(new { error = "A transaction with this external ID already exists in this organization." });

        var transaction = new CustomerTransaction
        {
            OrganizationId = customer.OrganizationId,
            CustomerId = customer.Id,
            ExternalTransactionId = externalId,
            OrderDate = request.OrderDate,
            Amount = request.Amount,
            Category = Clean(request.Category),
            Channel = Clean(request.Channel),
            IsSynthetic = false
        };
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(customer.OrganizationId, "transaction.created", "CustomerTransaction", transaction.Id.ToString(), cancellationToken: cancellationToken);
        return Created($"/api/customers/{customerId}/transactions/{transaction.Id}", new
        {
            transaction.Id,
            transaction.CustomerId,
            transaction.ExternalTransactionId,
            transaction.OrderDate,
            transaction.Amount,
            transaction.Category,
            transaction.Channel,
            transaction.IsSynthetic
        });
    }

    [HttpGet("interactions")]
    public async Task<IActionResult> GetInteractions(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        if (!await CanAccessCustomerAsync(customerId, cancellationToken))
            return NotFound();
        var query = database.Interactions.AsNoTracking().Where(row => row.CustomerId == customerId);
        var interactions = await query.OrderByDescending(row => row.OccurredAtUtc).Take(100)
            .Select(row => new { row.Id, row.OccurredAtUtc, row.Type, row.Summary, row.Outcome, row.IsSynthetic })
            .ToListAsync(cancellationToken);
        return Ok(interactions);
    }

    [HttpPost("interactions")]
    public async Task<IActionResult> CreateInteraction(
        Guid customerId,
        InteractionWriteRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);
        var customer = await FindAccessibleCustomerAsync(customerId, cancellationToken);
        if (customer is null)
            return NotFound();
        var occurredAt = request.OccurredAtUtc?.UtcDateTime ?? DateTime.UtcNow;
        if (occurredAt > DateTime.UtcNow.AddMinutes(5))
            return BadRequest(new { error = "OccurredAtUtc cannot be in the future." });

        var interaction = new CustomerInteraction
        {
            OrganizationId = customer.OrganizationId,
            CustomerId = customer.Id,
            OccurredAtUtc = occurredAt,
            Type = request.Type.Trim().ToLowerInvariant(),
            Summary = Clean(request.Summary),
            Outcome = Clean(request.Outcome),
            RecordedByUserId = tenant.RequireUserId(),
            IsSynthetic = false
        };
        database.Interactions.Add(interaction);
        await database.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(customer.OrganizationId, "interaction.created", "CustomerInteraction", interaction.Id.ToString(), cancellationToken: cancellationToken);
        return Created($"/api/customers/{customerId}/interactions/{interaction.Id}", new
        {
            interaction.Id,
            interaction.CustomerId,
            interaction.OccurredAtUtc,
            interaction.Type,
            interaction.Summary,
            interaction.Outcome,
            interaction.IsSynthetic
        });
    }

    [HttpGet("follow-ups")]
    public async Task<IActionResult> GetFollowUps(Guid customerId, CancellationToken cancellationToken = default)
    {
        if (!await CanAccessCustomerAsync(customerId, cancellationToken))
            return NotFound();
        var query = database.FollowUps.AsNoTracking().Where(row => row.CustomerId == customerId);
        if (User.IsInRole(Roles.SalesExecutive))
            query = query.Where(row => row.OwnerUserId == tenant.UserId);
        var followUps = await query.OrderBy(row => row.DueAtUtc).Take(100)
            .Select(row => new { row.Id, row.Subject, row.DueAtUtc, row.CompletedAtUtc, row.OwnerUserId })
            .ToListAsync(cancellationToken);
        return Ok(followUps);
    }

    [HttpPost("follow-ups")]
    public async Task<IActionResult> CreateFollowUp(
        Guid customerId,
        FollowUpWriteRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);
        var customer = await FindAccessibleCustomerAsync(customerId, cancellationToken);
        if (customer is null)
            return NotFound();
        if (request.DueAtUtc == default)
            return BadRequest(new { error = "DueAtUtc is required." });
        if (request.OwnerUserId.HasValue && User.IsInRole(Roles.SalesExecutive))
            return Forbid();

        var ownerUserId = request.OwnerUserId ?? tenant.RequireUserId();
        var validOwner = await database.Users.AnyAsync(
            row => row.Id == ownerUserId && row.OrganizationId == customer.OrganizationId && row.IsActive,
            cancellationToken);
        if (!validOwner)
            return BadRequest(new { error = "ownerUserId must be an active user in this organization." });

        var followUp = new CustomerFollowUp
        {
            OrganizationId = customer.OrganizationId,
            CustomerId = customer.Id,
            Subject = request.Subject.Trim(),
            DueAtUtc = request.DueAtUtc.UtcDateTime,
            OwnerUserId = ownerUserId
        };
        database.FollowUps.Add(followUp);
        await database.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(customer.OrganizationId, "followup.created", "CustomerFollowUp", followUp.Id.ToString(), cancellationToken: cancellationToken);
        return Created($"/api/customers/{customerId}/follow-ups/{followUp.Id}", new
        {
            followUp.Id,
            followUp.CustomerId,
            followUp.Subject,
            followUp.DueAtUtc,
            followUp.CompletedAtUtc,
            followUp.OwnerUserId
        });
    }

    [HttpPatch("follow-ups/{followUpId:guid}/complete")]
    public async Task<IActionResult> CompleteFollowUp(
        Guid customerId,
        Guid followUpId,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessCustomerAsync(customerId, cancellationToken))
            return NotFound();
        var query = database.FollowUps.Where(row => row.Id == followUpId && row.CustomerId == customerId);
        if (User.IsInRole(Roles.SalesExecutive))
            query = query.Where(row => row.OwnerUserId == tenant.UserId);
        var followUp = await query.SingleOrDefaultAsync(cancellationToken);
        if (followUp is null)
            return NotFound();
        if (!followUp.CompletedAtUtc.HasValue)
        {
            followUp.CompletedAtUtc = DateTime.UtcNow;
            followUp.UpdatedAtUtc = DateTime.UtcNow;
            await database.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync(followUp.OrganizationId, "followup.completed", "CustomerFollowUp", followUp.Id.ToString(), cancellationToken: cancellationToken);
        }
        return Ok(new { followUp.Id, followUp.CompletedAtUtc });
    }

    [HttpPost("notes")]
    public async Task<IActionResult> CreateNote(
        Guid customerId,
        CustomerNoteWriteRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);
        var customer = await FindAccessibleCustomerAsync(customerId, cancellationToken);
        if (customer is null)
            return NotFound();
        var note = new CustomerNote
        {
            OrganizationId = customer.OrganizationId,
            CustomerId = customer.Id,
            Body = request.Body.Trim(),
            AuthorUserId = tenant.RequireUserId()
        };
        database.CustomerNotes.Add(note);
        await database.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(customer.OrganizationId, "customer.note.created", "CustomerNote", note.Id.ToString(), cancellationToken: cancellationToken);
        return Created($"/api/customers/{customerId}/notes/{note.Id}", new { note.Id, note.Body, note.CreatedAtUtc });
    }

    private async Task<bool> CanAccessCustomerAsync(Guid customerId, CancellationToken cancellationToken) =>
        await FindAccessibleCustomerAsync(customerId, cancellationToken) is not null;

    private async Task<Customer?> FindAccessibleCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var query = database.Customers.Where(row => row.Id == customerId && row.Status != "Archived");
        if (User.IsInRole(Roles.SalesExecutive))
            query = query.Where(row => row.OwnerUserId == tenant.UserId);
        return await query.SingleOrDefaultAsync(cancellationToken);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
