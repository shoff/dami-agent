using System.Globalization;
using System.Net;

namespace Dami.Authentication;

/// <summary>Runs the authorization-code + PKCE flow against the local host.</summary>
/// <remarks>
/// The desktop counterpart to <see cref="DeviceLogin"/>. There is no browser hand-off
/// here: the host has no HTML login page, so the client collects the credentials itself,
/// posts them with the authorization request, and reads the code out of the redirect
/// instead of following it — nothing listens at the registered redirect URI and nothing
/// needs to.
/// </remarks>
public sealed class PkceLogin
{
    private const string CLIENT_ID = "dami-gui";

    private readonly HttpClient http;

    /// <summary>Creates the login over a client that must not follow redirects.</summary>
    public PkceLogin(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        this.http = http;
    }

    /// <summary>Creates an HTTP client suitable for this flow.</summary>
    /// <remarks>
    /// AllowAutoRedirect=false is the flow, not a tuning knob: the authorization code
    /// arrives in a Location header pointing at a loopback URI where nothing listens. A
    /// client that follows it throws the code away and reports a connection failure
    /// instead of a login.
    /// </remarks>
    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
    });

    /// <summary>Authorizes with the given account and exchanges the code for a token.</summary>
    public async Task<DevicePoll> LogInAsync(
        Uri host, Uri redirectUri, string username, string password,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(redirectUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrEmpty(password);

        var verifier = PkceFlow.CreateVerifier();
        var state = PkceFlow.CreateVerifier();
        var (code, refusal) = await this.AuthorizeAsync(
            host, AuthorizationForm(redirectUri, username, password, PkceFlow.Challenge(verifier), state),
            state, cancellationToken).ConfigureAwait(false);

        return refusal
            ?? await this.ExchangeAsync(host, redirectUri, code!, verifier, cancellationToken)
                .ConfigureAwait(false);
    }

    private async Task<(string? Code, DevicePoll? Refusal)> AuthorizeAsync(
        Uri host, FormUrlEncodedContent form, string state, CancellationToken cancellationToken)
    {
        using var response = await this.http.PostAsync(
            new Uri(host, "/connect/authorize"), form, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return (null, new DevicePoll(
                DevicePollResult.Denied, null, "the host did not accept that username and password"));
        }

        if ((int)response.StatusCode is < 300 or >= 400 || response.Headers.Location is null)
        {
            return (null, new DevicePoll(DevicePollResult.Failed, null, string.Create(
                CultureInfo.InvariantCulture,
                $"expected a redirect from /connect/authorize, got {(int)response.StatusCode}")));
        }

        var callback = PkceFlow.ReadCallback(response.Headers.Location, state);
        return callback.Code is null
            ? (null, new DevicePoll(DevicePollResult.Failed, null, callback.Error))
            : (callback.Code, null);
    }

    private async Task<DevicePoll> ExchangeAsync(
        Uri host, Uri redirectUri, string code, string verifier, CancellationToken cancellationToken)
    {
        using var response = await this.http.PostAsync(
            new Uri(host, "/connect/token"),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["client_id"] = CLIENT_ID,
                ["redirect_uri"] = redirectUri.AbsoluteUri,
                ["code_verifier"] = verifier,
            }),
            cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return DeviceFlow.ReadPoll(body);
    }

    private static FormUrlEncodedContent AuthorizationForm(
        Uri redirectUri, string username, string password, string challenge, string state) =>
        new(new Dictionary<string, string>
        {
            ["client_id"] = CLIENT_ID,
            ["redirect_uri"] = redirectUri.AbsoluteUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', DamiAuthorizationScopes.RUNTIME_READ,
                DamiAuthorizationScopes.RUNTIME_WRITE, DamiAuthorizationScopes.APPROVALS_RESOLVE),
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["username"] = username,
            ["password"] = password,
        });
}
