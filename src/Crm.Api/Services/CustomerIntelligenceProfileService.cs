using Crm.Api.Data;
using Crm.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Crm.Api.Services;

public sealed class CustomerIntelligenceProfileService(CrmDbContext database)
{
    public async Task<IReadOnlyList<CustomerIntelligenceProfile>> GetProfilesAsync(
        bool includeSynthetic,
        Guid? ownerUserId,
        CancellationToken cancellationToken)
    {
        var query = database.Customers.AsNoTracking().Where(row => row.Status != "Archived");
        if (!includeSynthetic)
            query = query.Where(row => !row.IsSynthetic);
        if (ownerUserId.HasValue)
            query = query.Where(row => row.OwnerUserId == ownerUserId.Value);

        var customers = await query
            .Select(row => new CustomerIdentity(row.Id, row.Status, row.SignupDate, row.IsSynthetic))
            .ToListAsync(cancellationToken);
        return await BuildProfilesAsync(customers, cancellationToken);
    }

    public async Task<CustomerIntelligenceProfile?> GetProfileAsync(
        Guid customerId,
        Guid? ownerUserId,
        CancellationToken cancellationToken)
    {
        var query = database.Customers.AsNoTracking()
            .Where(row => row.Id == customerId && row.Status != "Archived");
        if (ownerUserId.HasValue)
            query = query.Where(row => row.OwnerUserId == ownerUserId.Value);

        var customer = await query
            .Select(row => new CustomerIdentity(row.Id, row.Status, row.SignupDate, row.IsSynthetic))
            .SingleOrDefaultAsync(cancellationToken);
        if (customer is null)
            return null;
        return (await BuildProfilesAsync([customer], cancellationToken)).Single();
    }

    private async Task<IReadOnlyList<CustomerIntelligenceProfile>> BuildProfilesAsync(
        IReadOnlyList<CustomerIdentity> customers,
        CancellationToken cancellationToken)
    {
        if (customers.Count == 0)
            return [];

        var ids = customers.Select(row => row.Id).ToArray();
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var transactionStart = today.AddDays(-365);
        var interactionStart = now.AddDays(-90);

        var transactions = await database.Transactions.AsNoTracking()
            .Where(row => ids.Contains(row.CustomerId)
                && row.OrderDate >= transactionStart && row.OrderDate <= today)
            .GroupBy(row => row.CustomerId)
            .Select(group => new
            {
                CustomerId = group.Key,
                Orders = group.Count(),
                Spend = group.Sum(row => row.Amount),
                LastPurchase = group.Max(row => row.OrderDate)
            })
            .ToListAsync(cancellationToken);
        var transactionByCustomer = transactions.ToDictionary(row => row.CustomerId);

        var interactions = await database.Interactions.AsNoTracking()
            .Where(row => ids.Contains(row.CustomerId) && row.OccurredAtUtc >= interactionStart && row.OccurredAtUtc <= now)
            .GroupBy(row => new { row.CustomerId, row.Type })
            .Select(group => new { group.Key.CustomerId, group.Key.Type, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var interactionByCustomer = interactions
            .GroupBy(row => row.CustomerId)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    SupportCases = group.Where(row => row.Type.ToLower() is "support_case" or "support")
                        .Sum(row => row.Count),
                    Enquiries = group.Where(row => row.Type.ToLower() is "enquiry" or "inquiry")
                        .Sum(row => row.Count)
                });

        var snapshots = await database.ActivitySnapshots.AsNoTracking()
            .Where(row => ids.Contains(row.CustomerId))
            .OrderByDescending(row => row.SnapshotDate)
            .Select(row => new
            {
                row.CustomerId, row.SnapshotDate, row.Sessions30d, row.ProductViews30d,
                row.CartAdds30d, row.CampaignEmails30d, row.CampaignClicks30d
            })
            .ToListAsync(cancellationToken);
        var activityByCustomer = snapshots
            .GroupBy(row => row.CustomerId)
            .ToDictionary(group => group.Key, group => group.First());

        var openFollowUps = await database.FollowUps.AsNoTracking()
            .Where(row => ids.Contains(row.CustomerId) && row.CompletedAtUtc == null)
            .GroupBy(row => row.CustomerId)
            .Select(group => new { CustomerId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(row => row.CustomerId, row => row.Count, cancellationToken);

        return customers.Select(customer =>
        {
            transactionByCustomer.TryGetValue(customer.Id, out var orderData);
            interactionByCustomer.TryGetValue(customer.Id, out var interactionData);
            activityByCustomer.TryGetValue(customer.Id, out var activity);

            var orders = orderData?.Orders ?? 0;
            var spend = orderData is null ? 0d : (double)orderData.Spend;
            var recency = orderData is null
                ? 365
                : Math.Clamp(today.DayNumber - orderData.LastPurchase.DayNumber, 0, 365);
            var emails = activity?.CampaignEmails30d ?? 0;
            var clicks = activity?.CampaignClicks30d ?? 0;
            var tenure = customer.SignupDate.HasValue
                ? Math.Clamp(today.DayNumber - customer.SignupDate.Value.DayNumber, 0, 36500)
                : 0;

            return new CustomerIntelligenceProfile(
                customer.Id,
                customer.Status,
                customer.SignupDate,
                customer.IsSynthetic,
                new CustomerFeatureDto(
                    customer.Id.ToString("N"),
                    recency,
                    orders,
                    spend,
                    orders > 0 ? spend / orders : 0,
                    activity?.Sessions30d ?? 0,
                    activity?.ProductViews30d ?? 0,
                    activity?.CartAdds30d ?? 0,
                    emails > 0 ? Math.Clamp((double)clicks / emails, 0, 1) : 0,
                    interactionData?.SupportCases ?? 0,
                    interactionData?.Enquiries ?? 0,
                    tenure),
                openFollowUps.GetValueOrDefault(customer.Id));
        }).ToArray();
    }

    private sealed record CustomerIdentity(Guid Id, string Status, DateOnly? SignupDate, bool IsSynthetic);
}
