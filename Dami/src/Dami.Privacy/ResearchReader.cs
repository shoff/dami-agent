using System.Net;
using System.Net.Sockets;
using Dami.Contracts.Events;
using Dami.Contracts.Privacy;
using Dami.Contracts.Research;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Privacy;

/// <summary>Reads public pages for research (ADR-0033), and refuses everything else.</summary>
/// <remarks>
/// The allowlisted client answers "may this host be spoken to?"; this one answers "is
/// this a public page?" — http or https, a name that resolves to a public address, not
/// on the blocked list, followed through at most five redirects each checked the same
/// way. A private address behind a public name is the classic way a fetcher becomes a
/// scanner of the network it sits on; the resolution check is what stops it.
/// </remarks>
public sealed class ResearchReader : IResearchReader
{
    private const string ACTOR = "research";
    private const int MAX_REDIRECTS = 5;

    private readonly HttpClient httpClient;
    private readonly IEgressBudget egressBudget;
    private readonly ResearchOptions researchOptions;
    private readonly IExecutionEventStore eventStore;
    private readonly TimeProvider clock;
    private readonly ILogger<ResearchReader> logger;

    /// <summary>Creates the reader.</summary>
    public ResearchReader(
        HttpClient httpClient,
        IEgressBudget egressBudget,
        IOptions<ResearchOptions> researchOptions,
        IExecutionEventStore eventStore,
        TimeProvider clock,
        ILogger<ResearchReader> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(egressBudget);
        ArgumentNullException.ThrowIfNull(researchOptions);
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.httpClient = httpClient;
        this.egressBudget = egressBudget;
        this.researchOptions = researchOptions.Value;
        this.eventStore = eventStore;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<ResearchPage> ReadAsync(Uri url, Guid traceId, ExecutionOrigin origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        await this.EmitAsync(traceId, origin, ExecutionEventType.EgressRequested, ExecutionStatus.Running,
            $"research read -> {url.Host}", cancellationToken).ConfigureAwait(false);
        var refusal = this.researchOptions.Enabled
            ? await this.egressBudget.FindRefusalAsync(cancellationToken).ConfigureAwait(false)
            : "web research is not enabled (Research:Enabled)";
        if (refusal is not null)
        {
            await this.RefuseAsync(traceId, origin, refusal, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await this.FollowAsync(url, traceId, origin, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            await this.EmitAsync(traceId, origin, ExecutionEventType.EgressFailed, ExecutionStatus.Failed,
                $"{url.Host} failed: {exception.Message}", cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Whether an address is one this host may read from: routable, and not ours.</summary>
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal
            || address.IsIPv6Multicast || address.IsIPv6UniqueLocal || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return true;
        }

        var b = address.GetAddressBytes();
        return !(b[0] == 10 || b[0] == 127 || b[0] == 0
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
            || b[0] >= 224);
    }

    private async Task<ResearchPage> FollowAsync(Uri url, Guid traceId, ExecutionOrigin origin, CancellationToken cancellationToken)
    {
        var destination = url;
        for (var redirects = 0; redirects <= MAX_REDIRECTS; redirects++)
        {
            await this.EnsurePublicAsync(destination, traceId, origin, cancellationToken).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(this.researchOptions.TimeoutSeconds));
            using var response = await this.httpClient
                .GetAsync(destination, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                destination = location.IsAbsoluteUri ? location : new Uri(destination, location);
                continue;
            }

            return await this.CompleteAsync(destination, response, traceId, origin, timeout.Token).ConfigureAwait(false);
        }

        throw new HttpRequestException($"research read exceeded the redirect limit of {MAX_REDIRECTS}");
    }

    private async Task EnsurePublicAsync(Uri destination, Guid traceId, ExecutionOrigin origin, CancellationToken cancellationToken)
    {
        var refusal = await this.FindRefusalAsync(destination, cancellationToken).ConfigureAwait(false);
        if (refusal is not null)
        {
            await this.RefuseAsync(traceId, origin, refusal, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string?> FindRefusalAsync(Uri destination, CancellationToken cancellationToken)
    {
        if (destination.Scheme != Uri.UriSchemeHttps && destination.Scheme != Uri.UriSchemeHttp)
        {
            return $"scheme '{destination.Scheme}' is not readable; research reads http and https only";
        }

        if (this.researchOptions.BlockedHosts.Any(host =>
            destination.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
            || destination.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase)))
        {
            return $"host '{destination.Host}' is blocked for research";
        }

        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(destination.Host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(destination.Host, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            return $"host '{destination.Host}' does not resolve";
        }

        return addresses.Length == 0 || addresses.Any(address => !IsPublic(address))
            ? $"host '{destination.Host}' is not a public address; research never reads this network"
            : null;
    }

    private async Task<ResearchPage> CompleteAsync(
        Uri destination, HttpResponseMessage response, Guid traceId, ExecutionOrigin origin, CancellationToken cancellationToken)
    {
        var length = response.Content.Headers.ContentLength;
        if (length > this.researchOptions.MaxResponseBytes)
        {
            await this.RefuseAsync(traceId, origin, $"{destination.Host} answered {length} bytes; the limit is {this.researchOptions.MaxResponseBytes}", cancellationToken)
                .ConfigureAwait(false);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var bounded = new BoundedResponseReadStream(stream, response.Content, this.researchOptions.MaxResponseBytes);
        using var reader = new StreamReader(bounded);
        var html = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var page = new ResearchPage(
            destination, (int)response.StatusCode, HtmlText.Title(html), HtmlText.Extract(html, this.researchOptions.MaxTextChars));
        await this.EmitAsync(traceId, origin, ExecutionEventType.EgressCompleted, ExecutionStatus.Succeeded,
            $"{destination.Host} answered {page.StatusCode}: {page.Text.Length} chars read", cancellationToken).ConfigureAwait(false);
        return page;
    }

    private async Task RefuseAsync(Guid traceId, ExecutionOrigin origin, string refusal, CancellationToken cancellationToken)
    {
        await this.EmitAsync(traceId, origin, ExecutionEventType.EgressRefused, ExecutionStatus.Failed, refusal, cancellationToken)
            .ConfigureAwait(false);
        this.logger.LogWarning("Research read refused: {Reason}", refusal);
        throw new EgressRefusedException(refusal);
    }

    private Task EmitAsync(
        Guid traceId, ExecutionOrigin origin, ExecutionEventType type, ExecutionStatus status, string label,
        CancellationToken cancellationToken) =>
        this.eventStore.AppendAsync(new ExecutionEvent(
            Guid.NewGuid(), traceId, Guid.NewGuid(), null, origin, ACTOR, type, status, this.clock.GetUtcNow(), label), cancellationToken);
}
