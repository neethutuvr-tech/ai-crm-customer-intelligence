namespace Crm.Api.Middleware;

public sealed class PythonServiceUnavailableMiddleware(RequestDelegate next, ILogger<PythonServiceUnavailableMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "The Python AI service is unavailable.");
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "The AI service is unavailable. Start ml_api.py and retry.",
                dependency = "Python AI service"
            });
        }
    }
}

