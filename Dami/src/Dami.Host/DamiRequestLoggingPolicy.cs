namespace Dami.Host;

/// <summary>Limits informational request records to work that can change runtime state.</summary>
public static class DamiRequestLoggingPolicy
{
    /// <summary>Returns whether a successful request merits an informational lifecycle record.</summary>
    public static bool ShouldLogInformation(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return !HttpMethods.IsGet(context.Request.Method)
            && !HttpMethods.IsHead(context.Request.Method)
            && !HttpMethods.IsOptions(context.Request.Method);
    }
}
