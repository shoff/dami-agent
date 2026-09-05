namespace Dami.Host;

/// <summary>Emits privacy-safe, correlated lifecycle records for every HTTP request.</summary>
public sealed class StructuredRequestLoggingMiddleware
{
    private readonly RequestDelegate next;
    private readonly ILogger<StructuredRequestLoggingMiddleware> logger;

    /// <summary>Creates the request logging middleware.</summary>
    public StructuredRequestLoggingMiddleware(
        RequestDelegate next, ILogger<StructuredRequestLoggingMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(logger);
        this.next = next;
        this.logger = logger;
    }

    /// <summary>Logs the request outcome while preserving the response behavior.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var shouldLogInformation = DamiRequestLoggingPolicy.ShouldLogInformation(context);
        using (this.logger.BeginScope(DamiRequestLogScope.Create(context)))
        {
            if (shouldLogInformation)
            {
                this.logger.LogInformation("Request started");
            }

            try
            {
                await this.next(context).ConfigureAwait(false);
                this.LogOutcome(context, stopwatch.ElapsedMilliseconds, shouldLogInformation);
            }
            catch (Exception failure)
            {
                this.LogFailed(failure, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }
    }

    private void LogOutcome(HttpContext context, long elapsedMilliseconds, bool shouldLogInformation)
    {
        if (shouldLogInformation)
        {
            this.logger.LogInformation(
                "Request completed with {StatusCode} in {ElapsedMilliseconds} ms; Dami trace {DamiTraceId}",
                context.Response.StatusCode, elapsedMilliseconds, this.DamiTraceId(context));
        }
        else if (context.Response.StatusCode >= StatusCodes.Status400BadRequest)
        {
            this.logger.LogWarning(
                "Request completed with {StatusCode} in {ElapsedMilliseconds} ms; Dami trace {DamiTraceId}",
                context.Response.StatusCode, elapsedMilliseconds, this.DamiTraceId(context));
        }
    }

    private void LogFailed(Exception failure, long elapsedMilliseconds)
    {
        this.logger.LogError(
            "Request failed with {FailureKind} in {ElapsedMilliseconds} ms",
            failure.GetType().Name, elapsedMilliseconds);
    }

    private string DamiTraceId(HttpContext context)
    {
        return context.Response.Headers["X-Dami-Trace"].FirstOrDefault() ?? string.Empty;
    }
}
