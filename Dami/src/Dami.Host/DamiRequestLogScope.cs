using System.Diagnostics;

namespace Dami.Host;

/// <summary>Builds the privacy-safe correlation fields attached to each HTTP request log.</summary>
public static class DamiRequestLogScope
{
    /// <summary>Creates fields that identify a request without retaining its body or query string.</summary>
    public static IReadOnlyList<KeyValuePair<string, object?>> Create(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return
        [
            new("RequestId", context.TraceIdentifier),
            new("RequestMethod", context.Request.Method),
            new("RequestPath", context.Request.Path.Value ?? string.Empty),
            new("TraceId", Activity.Current?.TraceId.ToHexString() ?? string.Empty),
        ];
    }
}
