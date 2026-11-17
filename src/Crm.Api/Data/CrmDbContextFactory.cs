using Crm.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Crm.Api.Data;

public sealed class CrmDbContextFactory : IDesignTimeDbContextFactory<CrmDbContext>
{
    public CrmDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CrmDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;
        return new CrmDbContext(options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? OrganizationId => null;
        public Guid? UserId => null;
        public string? Role => null;
        public Guid RequireOrganizationId() => throw new InvalidOperationException("Unavailable at design time.");
        public Guid RequireUserId() => throw new InvalidOperationException("Unavailable at design time.");
    }
}
