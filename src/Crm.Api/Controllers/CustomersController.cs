using Crm.Api.Data;
using Crm.Api.Models;
using Crm.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crm.Api.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize(Roles = "Admin,Manager,Sales Executive")]
public sealed class CustomersController(CrmDbContext database, ITenantContext tenant, AuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetCustomers(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string sortBy = "name",
        [FromQuery] bool descending = false,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            return BadRequest(new { error = "page must be positive and pageSize must be between 1 and 100." });
        if (!new[] { "name", "city", "signup", "created" }.Contains(sortBy, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { error = "sortBy must be name, city, signup or created." });
        if (!new[] { "Active", "Inactive", "At Risk" }.Contains(status ?? "", StringComparer.OrdinalIgnoreCase) && status is not null)
            return BadRequest(new { error = "status must be Active, Inactive or At Risk." });

        IQueryable<Customer> query = CustomerScope().AsNoTracking().Where(row => row.Status != "Archived");
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(row => row.Status == status.Trim());
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(row => row.Name.Contains(term)
                || (row.Email != null && row.Email.Contains(term))
                || (row.City != null && row.City.Contains(term))
                || (row.ExternalId != null && row.ExternalId.Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);
        query = (sortBy.ToLowerInvariant(), descending) switch
        {
            ("created", false) => query.OrderBy(row => row.CreatedAtUtc),
            ("created", true) => query.OrderByDescending(row => row.CreatedAtUtc),
            ("signup", false) => query.OrderBy(row => row.SignupDate),
            ("signup", true) => query.OrderByDescending(row => row.SignupDate),
            ("city", false) => query.OrderBy(row => row.City).ThenBy(row => row.Name),
            ("city", true) => query.OrderByDescending(row => row.City).ThenBy(row => row.Name),
            ("name", false) => query.OrderBy(row => row.Name),
            _ => query.OrderByDescending(row => row.Name)
        };
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(row => new CustomerSummaryDto(row.Id, row.Name, row.Email, row.Phone, row.SignupDate,
                row.City, row.AcquisitionChannel, row.Status, row.IsSynthetic, row.OwnerUserId,
                row.ExternalId, row.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        return Ok(new { items, page, pageSize, total, pages = (int)Math.Ceiling(total / (double)pageSize) });
    }

    [HttpGet("{customerId:guid}")]
    public async Task<IActionResult> GetCustomer(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await CustomerScope().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == customerId && row.Status != "Archived", cancellationToken);
        if (customer is null) return NotFound();

        var transactions = await database.Transactions.AsNoTracking()
            .Where(row => row.CustomerId == customerId)
            .OrderByDescending(row => row.OrderDate).Take(50)
            .Select(row => new { row.Id, row.ExternalTransactionId, row.OrderDate, row.Amount, row.Category, row.Channel, row.IsSynthetic })
            .ToListAsync(cancellationToken);
        var interactions = await database.Interactions.AsNoTracking()
            .Where(row => row.CustomerId == customerId)
            .OrderByDescending(row => row.OccurredAtUtc).Take(50)
            .Select(row => new { row.Id, row.OccurredAtUtc, row.Type, row.Summary, row.Outcome, row.IsSynthetic })
            .ToListAsync(cancellationToken);
        var notes = await database.CustomerNotes.AsNoTracking()
            .Where(row => row.CustomerId == customerId)
            .OrderByDescending(row => row.CreatedAtUtc).Take(50)
            .Select(row => new { row.Id, row.Body, row.CreatedAtUtc })
            .ToListAsync(cancellationToken);
        var followUps = await database.FollowUps.AsNoTracking()
            .Where(row => row.CustomerId == customerId)
            .OrderBy(row => row.DueAtUtc).Take(50)
            .Select(row => new { row.Id, row.Subject, row.DueAtUtc, row.CompletedAtUtc, row.OwnerUserId })
            .ToListAsync(cancellationToken);
        return Ok(new { customer = ToSummary(customer), transactions, interactions, notes, followUps });
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager,Sales Executive")]
    public async Task<IActionResult> CreateCustomer(CustomerWriteRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var organizationId = tenant.RequireOrganizationId();
        var ownerId = User.IsInRole(Roles.SalesExecutive) ? tenant.RequireUserId() : request.OwnerUserId;
        if (request.OwnerUserId.HasValue && User.IsInRole(Roles.SalesExecutive)) return Forbid();
        if (!await IsValidOwnerAsync(ownerId, organizationId, cancellationToken))
            return BadRequest(new { error = "ownerUserId must be an active Sales Executive in this organization." });

        var email = NormalizeEmail(request.Email);
        if (email is not null && await database.Customers.AnyAsync(row => row.Email == email, cancellationToken))
            return Conflict(new { error = "A customer with this email already exists in this organization." });
        if (request.ExternalId is not null && await database.Customers.AnyAsync(row => row.ExternalId == request.ExternalId.Trim(), cancellationToken))
            return Conflict(new { error = "A customer with this externalId already exists in this organization." });

        var customer = new Customer
        {
            OrganizationId = organizationId,
            Name = request.Name.Trim(),
            Email = email,
            Phone = Clean(request.Phone),
            SignupDate = request.SignupDate,
            City = Clean(request.City),
            AcquisitionChannel = Clean(request.AcquisitionChannel),
            Status = NormalizeStatus(request.Status),
            OwnerUserId = ownerId,
            ExternalId = Clean(request.ExternalId),
            IsSynthetic = false
        };
        database.Customers.Add(customer);
        await database.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(organizationId, "customer.created", "Customer", customer.Id.ToString(), cancellationToken: cancellationToken);
        return CreatedAtAction(nameof(GetCustomer), new { customerId = customer.Id }, ToSummary(customer));
    }

    [HttpPut("{customerId:guid}")]
    [Authorize(Roles = "Admin,Manager,Sales Executive")]
    public async Task<IActionResult> UpdateCustomer(Guid customerId, CustomerWriteRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var customer = await CustomerScope().SingleOrDefaultAsync(
            row => row.Id == customerId && row.Status != "Archived", cancellationToken);
        if (customer is null) return NotFound();
        var ownerId = User.IsInRole(Roles.SalesExecutive) ? customer.OwnerUserId : request.OwnerUserId;
        if (!await IsValidOwnerAsync(ownerId, customer.OrganizationId, cancellationToken))
            return BadRequest(new { error = "ownerUserId must be an active Sales Executive in this organization." });

        var email = NormalizeEmail(request.Email);
        if (email is not null && await database.Customers.AnyAsync(
                row => row.Email == email && row.Id != customer.Id, cancellationToken))
            return Conflict(new { error = "A customer with this email already exists in this organization." });
        var externalId = Clean(request.ExternalId);
        if (externalId is not null && await database.Customers.AnyAsync(
                row => row.ExternalId == externalId && row.Id != customer.Id, cancellationToken))
            return Conflict(new { error = "A customer with this externalId already exists in this organization." });

        customer.Name = request.Name.Trim();
        customer.Email = email;
        customer.Phone = Clean(request.Phone);
        customer.SignupDate = request.SignupDate;
        customer.City = Clean(request.City);
        customer.AcquisitionChannel = Clean(request.AcquisitionChannel);
        customer.Status = NormalizeStatus(request.Status);
        customer.OwnerUserId = ownerId;
        customer.ExternalId = externalId;
        customer.UpdatedAtUtc = DateTime.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(customer.OrganizationId, "customer.updated", "Customer", customer.Id.ToString(), cancellationToken: cancellationToken);
        return Ok(ToSummary(customer));
    }

    [HttpDelete("{customerId:guid}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> ArchiveCustomer(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await database.Customers.SingleOrDefaultAsync(row => row.Id == customerId, cancellationToken);
        if (customer is null || customer.Status == "Archived") return NotFound();
        customer.Status = "Archived";
        customer.UpdatedAtUtc = DateTime.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(customer.OrganizationId, "customer.archived", "Customer", customer.Id.ToString(), cancellationToken: cancellationToken);
        return NoContent();
    }

    private IQueryable<Customer> CustomerScope()
    {
        var query = database.Customers.AsQueryable();
        return User.IsInRole(Roles.SalesExecutive)
            ? query.Where(row => row.OwnerUserId == tenant.UserId)
            : query;
    }

    private async Task<bool> IsValidOwnerAsync(Guid? ownerId, Guid organizationId, CancellationToken cancellationToken)
    {
        if (!ownerId.HasValue) return true;
        var owner = await database.Users.SingleOrDefaultAsync(
            row => row.Id == ownerId.Value && row.OrganizationId == organizationId && row.IsActive,
            cancellationToken);
        if (owner is null) return false;
        return await database.UserRoles.Join(database.Roles,
                userRole => userRole.RoleId, role => role.Id,
                (userRole, role) => new { userRole.UserId, role.Name })
            .AnyAsync(row => row.UserId == owner.Id && row.Name == Roles.SalesExecutive, cancellationToken);
    }

    private static string? NormalizeEmail(string? email) => string.IsNullOrWhiteSpace(email)
        ? null
        : email.Trim().ToLowerInvariant();
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string NormalizeStatus(string? value) => string.IsNullOrWhiteSpace(value) ? "Active" : value.Trim() switch
    {
        "active" or "Active" => "Active",
        "inactive" or "Inactive" => "Inactive",
        "at risk" or "At Risk" => "At Risk",
        _ => throw new ArgumentException("status must be Active, Inactive or At Risk.")
    };

    private static CustomerSummaryDto ToSummary(Customer row) => new(
        row.Id, row.Name, row.Email, row.Phone, row.SignupDate, row.City,
        row.AcquisitionChannel, row.Status, row.IsSynthetic, row.OwnerUserId,
        row.ExternalId, row.CreatedAtUtc);
}
