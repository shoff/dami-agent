using System.Globalization;

namespace Dami.Authentication;

/// <summary>What a completed or abandoned login came to.</summary>
public sealed record LoginOutcome(bool Succeeded, string Message);

/// <summary>Runs the RFC 8628 device flow against the local host.</summary>
/// <remarks>
/// The device flow rather than a password prompt because the CLI is a public client: it
/// runs on a machine its user controls and cannot hold a secret. The user approves in a
/// browser, the CLI only ever sees the resulting token.
///
/// Polling is bounded by the server's own expiry rather than a count, so a slow approval
/// is not cut short and an abandoned one does not poll forever.
/// </remarks>
public sealed class DeviceLogin
{
    private readonly HttpClient http;
    private readonly TimeProvider clock;

    /// <summary>Creates the login.</summary>
    public DeviceLogin(HttpClient http, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(clock);

        this.http = http;
        this.clock = clock;
    }

    /// <summary>Asks the host to start a device authorization.</summary>
    public async Task<DeviceAuthorization?> BeginAsync(Uri host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);

        using var response = await this.http.PostAsync(
            new Uri(host, "/connect/device"),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = "dami-cli",
                ["scope"] = string.Join(' ', DamiAuthorizationScopes.RUNTIME_READ,
                    DamiAuthorizationScopes.RUNTIME_WRITE, DamiAuthorizationScopes.APPROVALS_RESOLVE),
            }),
            cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return DeviceFlow.ReadAuthorization(body);
    }

    /// <summary>Polls until the user approves, denies, or the code expires.</summary>
    public async Task<DevicePoll> AwaitApprovalAsync(
        Uri host, DeviceAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(authorization);

        var interval = authorization.Interval;
        var deadline = this.clock.GetUtcNow() + authorization.ExpiresIn;

        while (this.clock.GetUtcNow() < deadline)
        {
            await Task.Delay(interval, this.clock, cancellationToken).ConfigureAwait(false);
            var poll = await this.PollAsync(host, authorization.DeviceCode, cancellationToken)
                .ConfigureAwait(false);

            if (poll.Result is not (DevicePollResult.Pending or DevicePollResult.SlowDown))
            {
                return poll;
            }

            interval = DeviceFlow.NextInterval(interval, poll.Result);
        }

        return new DevicePoll(DevicePollResult.Expired, null, "the code expired before approval");
    }

    private async Task<DevicePoll> PollAsync(Uri host, string deviceCode, CancellationToken cancellationToken)
    {
        using var response = await this.http.PostAsync(
            new Uri(host, "/connect/token"),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = deviceCode,
                ["client_id"] = "dami-cli",
            }),
            cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return DeviceFlow.ReadPoll(body);
    }

    /// <summary>What to tell the user while they approve.</summary>
    public static string Instructions(DeviceAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        var where = authorization.VerificationUriComplete ?? authorization.VerificationUri;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"""
            Open  {where}
            Code  {authorization.UserCode}

            Waiting for approval (expires in {authorization.ExpiresIn.TotalMinutes:F0} min)…
            """);
    }
}
