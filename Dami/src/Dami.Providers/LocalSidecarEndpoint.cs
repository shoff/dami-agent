namespace Dami.Providers;

internal static class LocalSidecarEndpoint
{
    public static Uri Parse(string baseUrl, string parameterName)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint) ||
            !endpoint.IsLoopback ||
            !IsHttp(endpoint))
        {
            throw new ArgumentException(
                "Local inference endpoints must be absolute HTTP URLs on loopback.",
                parameterName);
        }

        return endpoint;
    }

    private static bool IsHttp(Uri endpoint)
    {
        return endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps;
    }
}
