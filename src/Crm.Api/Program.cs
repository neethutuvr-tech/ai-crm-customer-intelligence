using Crm.Api.Middleware;
using Crm.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<ICustomerRepository, CsvCustomerRepository>();
builder.Services.AddHttpClient<IPredictionService, PythonPredictionService>(client =>
{
    var baseUrl = builder.Configuration["PythonService:BaseUrl"] ?? "http://127.0.0.1:8000/";
    client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
    client.Timeout = TimeSpan.FromSeconds(90);
});

var app = builder.Build();
app.UseMiddleware<PythonServiceUnavailableMiddleware>();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "crm-api" }));
app.MapControllers();
app.Run();

