using System.Text.Json;

namespace Dami.Authentication;

/// <summary>What the device authorization endpoint handed back (RFC 8628 §3.2).</summary>
public sealed record DeviceAuthorization(
    string DeviceCode,
    string UserCode,
    Uri VerificationUri,
    Uri? VerificationUriComplete,
    TimeSpan Interval,
    TimeSpan ExpiresIn);

/// <summary>What one poll of the token endpoint means.</summary>
public enum DevicePollResult
{
    /// <summary>Nobody has approved it yet. Keep waiting at the current interval.</summary>
    Pending,

    /// <summary>Polling too fast. The interval must increase (RFC 8628 §3.5).</summary>
    SlowDown,

    /// <summary>Approved; an access token came back.</summary>
    Granted,

    /// <summary>The user said no.</summary>
    Denied,

    /// <summary>The device code aged out before anyone approved it.</summary>
    Expired,

    /// <summary>Something else went wrong and retrying will not fix it.</summary>
    Failed,
}

/// <summary>One poll's outcome, with the token when there is one.</summary>
public sealed record DevicePoll(DevicePollResult Result, DamiToken? Token, string? Error);

/// <summary>Reads the device-flow wire format.</summary>
/// <remarks>
/// Pure, because the interesting mistakes here are in reading responses rather than in
/// making requests, and none of them should need a running authorization server to catch.
/// <c>slow_down</c> in particular is the one everybody gets wrong: it is an instruction to
/// back off, not an error, and treating it as a failure aborts a login that was going to
/// succeed — while treating it as an ordinary pending keeps hammering the endpoint that
/// just asked you to stop.
/// </remarks>
public static class DeviceFlow
{
    /// <summary>The interval to use when the server does not name one.</summary>
    public static readonly TimeSpan defaultInterval = TimeSpan.FromSeconds(5);

    /// <summary>Reads a device authorization response, or null if it is not one.</summary>
    public static DeviceAuthorization? ReadAuthorization(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (Text(root, "device_code") is not { Length: > 0 } device
                || Text(root, "user_code") is not { Length: > 0 } user
                || !Uri.TryCreate(Text(root, "verification_uri"), UriKind.Absolute, out var verify))
            {
                return null;
            }

            Uri.TryCreate(Text(root, "verification_uri_complete"), UriKind.Absolute, out var complete);
            return new DeviceAuthorization(
                device, user, verify, complete, Seconds(root, "interval", defaultInterval),
                Seconds(root, "expires_in", TimeSpan.FromMinutes(10)));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads one token-endpoint response into a decision.</summary>
    public static DevicePoll ReadPoll(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (Text(root, "access_token") is { Length: > 0 } access)
            {
                return new DevicePoll(
                    DevicePollResult.Granted,
                    new DamiToken(access, Text(root, "refresh_token"), Seconds(root, "expires_in", TimeSpan.FromHours(1))),
                    null);
            }

            var error = Text(root, "error") ?? string.Empty;
            return new DevicePoll(Classify(error), null, error.Length > 0 ? error : "no token and no error");
        }
        catch (JsonException)
        {
            return new DevicePoll(DevicePollResult.Failed, null, "the response was not JSON");
        }
    }

    private static DevicePollResult Classify(string error) => error switch
    {
        "authorization_pending" => DevicePollResult.Pending,
        "slow_down" => DevicePollResult.SlowDown,
        "access_denied" => DevicePollResult.Denied,
        "expired_token" => DevicePollResult.Expired,
        _ => DevicePollResult.Failed,
    };

    /// <summary>The next polling interval after a result.</summary>
    /// <remarks>
    /// RFC 8628 says a <c>slow_down</c> increases the interval by five seconds and that the
    /// increase persists. Returning to the original interval afterwards earns another
    /// slow_down immediately and turns the login into a fight with the server.
    /// </remarks>
    public static TimeSpan NextInterval(TimeSpan current, DevicePollResult result) =>
        result == DevicePollResult.SlowDown ? current + TimeSpan.FromSeconds(5) : current;

    private static string? Text(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

    private static TimeSpan Seconds(JsonElement root, string property, TimeSpan fallback) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? TimeSpan.FromSeconds(value.GetDouble())
            : fallback;
}
