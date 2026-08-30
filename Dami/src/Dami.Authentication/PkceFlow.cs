using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Dami.Authentication;

/// <summary>What the authorization redirect carried: a code, or why there is none.</summary>
public sealed record PkceCallback(string? Code, string? Error);

/// <summary>The pure parts of RFC 7636 — verifier, challenge, and redirect reading.</summary>
/// <remarks>
/// PKCE exists because a public client cannot keep a secret: the proof that the token
/// request comes from whoever started the authorization is a one-time hash preimage
/// instead. The parsing lives here, pure, for the same reason <see cref="DeviceFlow"/>'s
/// does — the interesting mistakes are in reading responses, and none of them should need
/// a running authorization server to catch.
/// </remarks>
public static class PkceFlow
{
    /// <summary>A fresh high-entropy code verifier (RFC 7636 §4.1).</summary>
    public static string CreateVerifier() =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>The S256 challenge for a verifier (RFC 7636 §4.2).</summary>
    public static string Challenge(string verifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifier);
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    /// <summary>Reads the code out of the redirect, or why there is none.</summary>
    /// <remarks>
    /// The state check is not an optional nicety: without it, anything that can hand the
    /// client a URL can hand it someone else's authorization code. A redirect whose state
    /// is missing or wrong is treated exactly like a refusal from the server.
    /// </remarks>
    public static PkceCallback ReadCallback(Uri location, string expectedState)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedState);

        var query = ParseQuery(location);
        if (query.TryGetValue("error", out var error))
        {
            return new PkceCallback(null, error.Length > 0 ? error : "error");
        }

        if (!query.TryGetValue("state", out var state) || state != expectedState)
        {
            return new PkceCallback(null, "the redirect did not carry the state this login sent");
        }

        return query.TryGetValue("code", out var code) && code.Length > 0
            ? new PkceCallback(code, null)
            : new PkceCallback(null, "the redirect carried no authorization code");
    }

    private static Dictionary<string, string> ParseQuery(Uri location)
    {
        // Location may be relative in theory; take whatever follows the question mark.
        var raw = location.IsAbsoluteUri ? location.Query : location.OriginalString;
        var mark = raw.IndexOf('?');
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in raw[(mark + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.Split('=', 2);
            values[Uri.UnescapeDataString(split[0])] =
                split.Length == 2 ? Uri.UnescapeDataString(split[1]) : string.Empty;
        }

        return values;
    }
}
