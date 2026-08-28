using System.Net.Http.Headers;

namespace Dami.Authentication;

/// <summary>Applies an acquired Dami access token to a runtime HTTP client.</summary>
public static class DamiBearerToken
{
    /// <summary>Sets bearer authentication when an access token is available.</summary>
    public static void Apply(HttpClient client, string? accessToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
        }
    }
}
