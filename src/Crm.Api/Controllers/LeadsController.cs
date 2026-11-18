using Crm.Api.Data;
using Crm.Api.Models;
using Crm.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crm.Api.Controllers;

[ApiController]
[Route("api/leads")]
[Authorize(Roles = "Admin,Manager,Sales Executive")]
public sealed class LeadsController(CrmDbContext database, ITenantContext tenant, AuditService audit) : ControllerBase
{
    private static readonly string[] EditableStatuses = ["New", "Contacted", "Qualified", "Disqualified"];

    [HttpGet]
    public async Task<IActionResult> GetLeads(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            return BadRequest(new { error = "page must be positive and pageSize must be between 1 and 100." });
        if (status is not null && !EditableStatuses.Contains(status, StringComparer.OrdinalIgnoreCase)
            && !string.Equals(status, "Converted", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "status must be New, Contacted, Qualified, Disqualified, or Converted." });

        var query = LeadScope().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(row => row.Status == status.Trim());
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(row => row.Name.Contains(term)
                || (row.Email != null && row.Email.Contains(term))
                || (row.Company != null && row.Company.Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(row => row.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(row => new LeadSummaryDto(row.Id, row.Name, row.Email, row.Phone, row.Company,
                row.Source, row.Status, row.OwnerUserId, row.ConvertedCustomerId, row.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        return Ok(new { items, page, pageSize, total, pages = (int)Math.Ceiling(total / (double)pageSize) });
    }

    [HttpGet("{leadId:guid}")]
    public async Task<IActionResult> GetLead(Guid leadId, CancellationToken cancellationToken)
    {
        var lead = await LeadScope().AsNoTracking().SingleOrDefaultAsync(row => row.Id == leadId, cancellationToken);
        return lead is null ? NotFound() : Ok(ToSummary(lead));
    }

    [HttpPost]
    public async Task<IActionResult> CreateLead(LeadWriteRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);
        var organizationId = tenant.RequireOrganizationId();
        var ownerId = User.IsInRole(Roles.SalesExecutive) ? tenant.RequireUserId() : request.OwnerUserId;
        if (request.OwnerUserId.HasValue && User.IsInRole(Roles.SalesExecutive))
            return Forbid();
        if (!await IsValidOwnerAsync(ownerId, organizationId, cancellationToken))
            return BadRequest(new { error = "ownerUserId must be an active Sales Executive in this organization." });

        var email = NormalizeEmail(request.Email);
        if (email is not null && await database.Leads.AnyAsync(row => row.Email == email, cancellationToken))
            return Conflict(new { error = "A lead with this email already exists in this organization." });

        var lead = new Lead
        {
            OrganizationId = organizationId,
            Name = request.Name.Trim(),
            Email = email,
            Phone = Clean(request.Phone),
            Company = Clean(request.Company),
            Source = Clean(request.Source),
            OwnerUserId = ownerId,
            Status = "New"
        };
        database.Leads.Add(lead);
        await database.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(organizationId, "lead.created", "Lead", lead.Id.ToString(), cancellationToken: cancellationToken);
        return CreatedAtAction(nameof(GetLead), new { leadId = lead.Id }, ToSummary(lead));
    }

    [HttpPut("{leadId:guid}")]
    public async Task<IActionResult> UpdateLead(Guid leadId, LeadWriteRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);
        var lead = await LeadScope().SingleOrDefaultAsync(row => row.Id == leadId, cancellationToken);
        if (lead is null)
            return NotFound();
        if (lead.Status == "Converted")
            return Conflict(new { error = "A converted lead cannot be edited." });

        var ownerId = User.IsInRole(Roles.SalesExecutive) ? lead.OwnerUserId : request.OwnerUserId;
        if (request.OwnerUserId.HasValue && User.IsInRole(Roles.SalesExecutive))
            return Forbid();
        if (!await IsValidOwnerAsync(ownerId, lead.OrganizationId, cancellationToken))
            return BadRequest(new { error = "ownerUserId must be an active Sales Executive in this organization." });

        var email = NormalizeEmail(request.Email);
        if (email is not null && await database.Leads.AnyAsync(
                row => row.Email == email && row.Id != lead.Id, cancellationToken))
            return Conflict(new { error = "A lead with this email already exists in this organization." });

        lead.Name = request.Name.Trim();
        lead.Email = email;
        lead.Phone = Clean(request.Phone);
        lead.Company = Clean(request.Company);
        lead.Source = Clean(request.Source);
        lead.OwnerUserId = ownerId;
        lead.UpdatedAtUtc = DateTime.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(lead.OrganizationId, "lead.updated", "Lead", lead.Id.ToString(), cancellationToken: cancellationToken);
        return Ok(ToSummary(lead));
    }

    [HttpPatch("{leadId:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid leadId,
        LeadStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || !EditableStatuses.Contains(request.Status, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { error = "status must be New, Contacted, Qualified, or Disqualified." });
        var lead = await LeadScope().SingleOrDefaultAsync(row => row.Id == leadId, cancellationToken);
        if (lead is null)
            return NotFound();
        if (lead.Status == "Converted")
            return Conflict(new { error = "A converted lead cannot change status." });

        lead.Status = EditableStatuses.First(value => string.Equals(value, request.Status, StringComparison.OrdinalIgnoreCase));
        lead.UpdatedAtUtc = DateTime.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(lead.OrganizationId, "lead.status.updated", "Lead", lead.Id.ToString(),
            new { status = lead.Status }, cancellationToken);
        return Ok(ToSummary(lead));
    }

    [HttpPost("{leadId:guid}/convert")]
    public async Task<IActionResult> ConvertLead(Guid leadId, CancellationToken cancellationToken)
    {
        var lead = await LeadScope().SingleOrDefaultAsync(row => row.Id == leadId, cancellationToken);
        if (lead is null)
            return NotFound();
        if (lead.Status == "Converted" || lead.ConvertedCustomerId.HasValue)
            return Conflict(new { error = "This lead has already been converted.", customerId = lead.ConvertedCustomerId });
        if (lead.Email is not null && await database.Customers.AnyAsync(row => row.Email == lead.Email, cancellationToken))
            return Conflict(new { error = "A customer with this lead's email already exists in this organization." });

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var customer = new Customer
        {
            OrganizationId = lead.OrganizationId,
            Name = lead.Name,
            Email = lead.Email,
            Phone = lead.Phone,
            AcquisitionChannel = lead.Source,
            Status = "Active",
            IsSynthetic = false,
            OwnerUserId = lead.OwnerUserId
        };
        database.Customers.Add(customer);
        await database.SaveChangesAsync(cancellationToken);
        lead.Status = "Converted";
        lead.ConvertedCustomerId = customer.Id;
        lead.UpdatedAtUtc = DateTime.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await audit.RecordAsync(lead.OrganizationId, "lead.converted", "Lead", lead.Id.ToString(),
            new { customerId = customer.Id }, cancellationToken);
        return Created($"/api/customers/{customer.Id}", new { leadId = lead.Id, customer = new CustomerSummaryDto(
            customer.Id, customer.Name, customer.Email, customer.Phone, customer.SignupDate, customer.City,
            customer.AcquisitionChannel, customer.Status, customer.IsSynthetic, customer.OwnerUserId,
            customer.ExternalId, customer.CreatedAtUtc) });
    }

    private IQueryable<Lead> LeadScope() => User.IsInRole(Roles.SalesExecutive)
        ? database.Leads.Where(row => row.OwnerUserId == tenant.UserId)
        : database.Leads;

    private async Task<bool> IsValidOwnerAsync(Guid? ownerId, Guid organizationId, CancellationToken cancellationToken)
    {
        if (!ownerId.HasValue)
            return true;
        var owner = await database.Users.SingleOrDefaultAsync(
            row => row.Id == ownerId.Value && row.OrganizationId == organizationId && row.IsActive,
            cancellationToken);
        if (owner is null)
            return false;
        return await database.UserRoles.Join(database.Roles,
                userRole => userRole.RoleId, role => role.Id,
                (userRole, role) => new { userRole.UserId, role.Name })
            .AnyAsync(row => row.UserId == owner.Id && row.Name == Roles.SalesExecutive, cancellationToken);
    }

    private static string? NormalizeEmail(string? email) => string.IsNullOrWhiteSpace(email)
        ? null
        : email.Trim().ToLowerInvariant();
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static LeadSummaryDto ToSummary(Lead row) => new(
        row.Id, row.Name, row.Email, row.Phone, row.Company, row.Source, row.Status,
        row.OwnerUserId, row.ConvertedCustomerId, row.CreatedAtUtc);
}
