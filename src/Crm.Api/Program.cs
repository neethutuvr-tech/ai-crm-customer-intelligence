using System.Threading.RateLimiting;
using Crm.Api.Data;
using Crm.Api.Middleware;
using Crm.Api.Models;
using Crm.Api.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var databasePath = builder.Configuration["Database:Path"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "ai-crm.db");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
var keyDirectory = builder.Configuration["DataProtection:KeyPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "KeysProtected");
Directory.CreateDirectory(keyDirectory);
var dataProtection = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
if (OperatingSystem.IsWindows())
{
    dataProtection.ProtectKeysWithDpapi();
}
dataProtection.SetApplicationName("AiCrmCustomerIntelligence");
builder.Services.AddDbContext<CrmDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredUniqueChars = 4;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
        options.SignIn.RequireConfirmedEmail = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<CrmDbContext>()
    .AddDefaultTokenProviders();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, ApplicationUserClaimsPrincipalFactory>();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "ai-crm.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.LoginPath = "/api/auth/login";
    options.AccessDeniedPath = "/api/auth/forbidden";
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<CustomerIntelligenceProfileService>();
builder.Services.AddScoped<IAppEmailSender, ConfiguredEmailSender>();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "ai-crm.csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 12,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

builder.Services.AddControllers();
builder.Services.AddHttpClient<IPredictionService, PythonPredictionService>(client =>
{
    var baseUrl = builder.Configuration["PythonService:BaseUrl"] ?? "http://127.0.0.1:8000/";
    client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
    client.Timeout = TimeSpan.FromSeconds(90);
});

var app = builder.Build();
app.UseMiddleware<PythonServiceUnavailableMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    var methodIsUnsafe = HttpMethods.IsPost(context.Request.Method)
        || HttpMethods.IsPut(context.Request.Method)
        || HttpMethods.IsPatch(context.Request.Method)
        || HttpMethods.IsDelete(context.Request.Method);
    var isPublicAuthRoute = context.Request.Path.StartsWithSegments("/api/auth/register")
        || context.Request.Path.StartsWithSegments("/api/auth/login")
        || context.Request.Path.StartsWithSegments("/api/auth/password-reset");
    if (methodIsUnsafe && !isPublicAuthRoute && context.User.Identity?.IsAuthenticated == true)
    {
        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "A valid X-CSRF-TOKEN header is required." });
            return;
        }
    }
    await next();
});
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "crm-api", database = "sqlite" }));
app.MapControllers();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
    await db.Database.MigrateAsync();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    foreach (var roleName in Roles.All)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
            await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
    }
}

app.Run();

public partial class Program;
