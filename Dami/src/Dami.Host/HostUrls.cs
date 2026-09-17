using System.Net;

namespace Dami.Host;

/// <summary>
/// Where <c>Dami.Host</c> listens. Loopback by default (D-005); anything else only with
/// authentication on (ADR-0020), refused at startup otherwise — the deferred exposure
/// decision, enforced in code rather than remembered.
/// </summary>
public static class HostUrls
{
    /// <summary>The default: the API on loopback, port 5810.</summary>
    public const string DEFAULT = "http://127.0.0.1:5810";

    /// <summary>The configuration key, a semicolon-separated list of URLs.</summary>
    public const string KEY = "Host:Urls";

    /// <summary>The URLs to listen on, validated against the authentication setting.</summary>
    /// <exception cref="InvalidOperationException">A URL is malformed, or leaves loopback while authentication is off.</exception>
    public static string Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var urls = configuration[KEY];
        if (string.IsNullOrWhiteSpace(urls))
        {
            return DEFAULT;
        }

        var authenticated = configuration.GetValue<bool>("Authentication:Enabled");
        foreach (var url in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
            {
                throw new InvalidOperationException($"{KEY} holds '{url}', which is not a URL.");
            }

            if (!IsLoopback(uri) && !authenticated)
            {
                throw new InvalidOperationException(
                    $"{KEY} holds '{url}', which is not loopback, and Authentication:Enabled is false. "
                    + "D-005 keeps the API on this host until it is authenticated (ADR-0020); "
                    + "reach the web view over an SSH tunnel or the tools/lan-proxy stack instead.");
            }
        }

        return urls;
    }

    private static bool IsLoopback(Uri uri)
    {
        if (uri.IsLoopback)
        {
            return true;
        }

        return IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
    }
}
