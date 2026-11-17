using Crm.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Crm.Api.Tests;

public sealed class CrmApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();
        builder.UseEnvironment("Testing");
        builder.UseSetting("DataProtection:KeyPath", Path.Combine(Path.GetTempPath(), "ai-crm-test-keys", Guid.NewGuid().ToString("N")));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<CrmDbContext>();
            services.RemoveAll<DbContextOptions<CrmDbContext>>();
            services.AddSingleton(_connection);
            services.AddDbContext<CrmDbContext>((provider, options) =>
                options.UseSqlite(provider.GetRequiredService<SqliteConnection>()));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
