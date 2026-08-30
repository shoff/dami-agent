using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Dami.Gateway.Discord;

/// <summary>Posts replies to Discord over its REST API.</summary>
/// <remarks>
/// Its own <see cref="HttpClient"/> rather than the egress client, which is the point of
/// ADR-0024: <see cref="Dami.Contracts.Privacy.IEgressClient"/> cannot express a body,
/// and widening it so this one caller could post a message would remove that guarantee
/// from every other caller. The channel is the audit point instead.
///
/// Rate limits are answered rather than ignored. The interest scout spent six passes
/// reporting success while a server returned 429 at it, which is exactly the failure this
/// retry exists to avoid repeating.
/// </remarks>
public sealed class DiscordRest : IDiscordRest
{
    private const string API = "https://discord.com/api/v10";

    private readonly HttpClient http;
    private readonly ILogger<DiscordRest> logger;

    /// <summary>Creates the client.</summary>
    public DiscordRest(HttpClient http, string token, ILogger<DiscordRest> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentNullException.ThrowIfNull(logger);

        this.http = http;
        this.logger = logger;
        this.http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", token);
    }

    /// <inheritdoc />
    public async Task PostMessageAsync(string channelId, string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);
        ArgumentNullException.ThrowIfNull(text);

        var url = $"{API}/channels/{channelId}/messages";
        using var response = await this.http
            .PostAsJsonAsync(url, new { content = Truncate(text) }, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            await this.RetryAfterAsync(url, text, response, cancellationToken).ConfigureAwait(false);
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    private async Task RetryAfterAsync(
        string url, string text, HttpResponseMessage refused, CancellationToken cancellationToken)
    {
        var wait = refused.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
        this.logger.LogWarning(
            "Discord rate-limited a reply; waiting {Seconds}s",
            wait.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture));

        await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        using var retry = await this.http
            .PostAsJsonAsync(url, new { content = Truncate(text) }, cancellationToken)
            .ConfigureAwait(false);
        retry.EnsureSuccessStatusCode();
    }

    /// <summary>Discord refuses anything past 2000 characters, so say so rather than fail.</summary>
    public static string Truncate(string text)
    {
        const int limit = 2000;
        const string mark = "… (truncated)";
        return text.Length <= limit ? text : string.Concat(text.AsSpan(0, limit - mark.Length), mark);
    }
}
