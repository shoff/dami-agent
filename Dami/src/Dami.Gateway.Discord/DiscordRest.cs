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

    /// <summary>Hosts Discord serves attachments from. Anything else is not ours to fetch.</summary>
    /// <remarks>
    /// The attachment URL arrives inside a gateway frame and is therefore remote input.
    /// Fetching whatever it names would make this a general-purpose request forwarder
    /// sitting inside the host — the shape of an SSRF, terminating in a local model whose
    /// output is then egressed. `IEgressClient` refuses unknown hosts for the same reason;
    /// the channel's own transport has to refuse them too rather than inherit nothing.
    /// </remarks>
    private static readonly string[] attachmentHosts =
    [
        "cdn.discordapp.com",
        "media.discordapp.net",
    ];

    /// <summary>Ceiling on a fetched attachment, enforced while reading.</summary>
    /// <remarks>
    /// The `size` Discord declares is remote input too, so it may not be the thing the
    /// limit is applied to. This is measured against bytes actually received.
    /// </remarks>
    private const int MAX_ATTACHMENT_BYTES = 16 * 1024 * 1024;

    private readonly HttpClient http;
    private readonly AuthenticationHeaderValue credential;
    private readonly ILogger<DiscordRest> logger;

    /// <summary>Creates the client.</summary>
    /// <remarks>
    /// The token is attached per request rather than to
    /// <c>DefaultRequestHeaders</c>, because a default is attached to *every* request the
    /// client makes — including the attachment download, whose host is Discord's CDN and
    /// which never asked for a credential. A test pins that it is not sent there.
    /// </remarks>
    public DiscordRest(HttpClient http, string token, ILogger<DiscordRest> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentNullException.ThrowIfNull(logger);

        this.http = http;
        this.credential = new AuthenticationHeaderValue("Bot", token);
        this.logger = logger;
    }

    /// <summary>An authenticated request to Discord's own API.</summary>
    private HttpRequestMessage Api(HttpMethod method, string url, HttpContent content) =>
        new(method, new Uri(url)) { Headers = { Authorization = this.credential }, Content = content };

    private static HttpContent Json(string text) =>
        JsonContent.Create(new { content = Truncate(text) });

    /// <inheritdoc />
    public async Task PostMessageAsync(string channelId, string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);
        ArgumentNullException.ThrowIfNull(text);

        var url = $"{API}/channels/{channelId}/messages";
        using var request = this.Api(HttpMethod.Post, url, Json(text));
        using var response = await this.http
            .SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            await this.RetryAfterAsync(url, text, response, cancellationToken).ConfigureAwait(false);
            return;
        }

        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task PostMessageWithFilesAsync(
        string channelId,
        string text,
        IReadOnlyList<Dami.Contracts.Privacy.OutboundAttachment> files,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(files);

        if (files.Count == 0)
        {
            await this.PostMessageAsync(channelId, text, cancellationToken).ConfigureAwait(false);
            return;
        }

        // multipart/form-data with a payload_json part is Discord's documented shape for
        // an upload; the files are indexed and referenced by that index.
        using var form = new MultipartFormDataContent();
        form.Add(
            new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new { content = Truncate(text) })),
            "payload_json");
        for (var index = 0; index < files.Count; index++)
        {
            var file = new ByteArrayContent(files[index].Bytes.ToArray());
            file.Headers.ContentType = new MediaTypeHeaderValue(files[index].ContentType);
            form.Add(file, $"files[{index}]", files[index].FileName);
        }

        using var request = this.Api(
            HttpMethod.Post, $"{API}/channels/{channelId}/messages", form);
        using var response = await this.http
            .SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<byte>> DownloadAsync(
        string url, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var destination)
            || destination.Scheme != Uri.UriSchemeHttps
            || !attachmentHosts.Contains(destination.Host, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"refusing to fetch an attachment from '{url}': not an https Discord CDN URL");
        }

        // Deliberately not built by Api(): the URL already carries its own signature, and
        // sending credentials to a host that did not ask for them is how they leak.
        using var request = new HttpRequestMessage(HttpMethod.Get, destination);
        using var response = await this.http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await ReadCappedAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads at most the ceiling, measuring what arrives rather than what was claimed.</summary>
    private static async Task<ReadOnlyMemory<byte>> ReadCappedAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MAX_ATTACHMENT_BYTES)
            {
                throw new InvalidOperationException(
                    $"attachment exceeded {MAX_ATTACHMENT_BYTES} bytes; refusing to buffer it");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return buffer.ToArray();
    }

    private async Task RetryAfterAsync(
        string url, string text, HttpResponseMessage refused, CancellationToken cancellationToken)
    {
        var wait = refused.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
        this.logger.LogWarning(
            "Discord rate-limited a reply; waiting {Seconds}s",
            wait.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture));

        await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        using var request = this.Api(HttpMethod.Post, url, Json(text));
        using var retry = await this.http
            .SendAsync(request, cancellationToken).ConfigureAwait(false);
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
