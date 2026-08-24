using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Privacy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Privacy;

/// <summary>Enforces and meters the dedicated request-body egress door used by MCP.</summary>
public sealed class McpEgressHttpMessageHandler : DelegatingHandler, IMcpEgressHttpHandler
{
    private const string ACTOR = "egress-mcp";
    private const int MAX_BODY_BYTES = 16 * 1024 * 1024;

    private readonly IEgressOperationContextReader contextReader;
    private readonly IEgressBudget budget;
    private readonly IReadOnlyList<string> allowedHosts;
    private readonly IReadOnlyList<string> forbiddenFragments;
    private readonly int maxRequestBytes;
    private readonly int maxResponseBytes;
    private readonly IExecutionEventStore eventStore;
    private readonly TimeProvider clock;
    private readonly ILogger<McpEgressHttpMessageHandler> logger;

    /// <summary>Creates a fail-closed MCP egress handler around the network transport.</summary>
    public McpEgressHttpMessageHandler(
        HttpMessageHandler innerHandler,
        IEgressOperationContextReader contextReader,
        IEgressBudget budget,
        IOptions<EgressOptions> options,
        IExecutionEventStore eventStore,
        TimeProvider clock,
        ILogger<McpEgressHttpMessageHandler> logger)
        : base(innerHandler)
    {
        EnsureRedirectsDisabled(innerHandler);
        ArgumentNullException.ThrowIfNull(contextReader);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        EgressOptions configured = options.Value;
        if (configured.MaxRequestBytes is < 1 or > MAX_BODY_BYTES
            || configured.MaxResponseBytes is < 1 or > MAX_BODY_BYTES)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"MCP egress limits must be between 1 and {MAX_BODY_BYTES} bytes.");
        }

        this.contextReader = contextReader;
        this.budget = budget;
        this.allowedHosts = SnapshotStrings(configured.AllowedHosts, nameof(options));
        this.forbiddenFragments = SnapshotStrings(configured.ForbiddenFragments, nameof(options));
        this.maxRequestBytes = configured.MaxRequestBytes;
        this.maxResponseBytes = configured.MaxResponseBytes;
        this.eventStore = eventStore;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EgressOperationContext context = this.contextReader.Current
            ?? throw new EgressRefusedException("MCP egress requires an explicit operation context.");
        Uri destination = request.RequestUri
            ?? throw new InvalidOperationException("MCP HTTP requests require a destination.");
        var egressSpanId = Guid.NewGuid();
        await this.EmitAsync(
            context, egressSpanId, ExecutionEventType.EgressRequested, ExecutionStatus.Running,
            $"{context.Purpose} -> {destination.Host}", cancellationToken).ConfigureAwait(false);
        await this.EnsureAllowedAsync(
            context, egressSpanId, destination, request.Content, cancellationToken)
            .ConfigureAwait(false);
        HttpResponseMessage response = await this.SendNetworkAsync(
            context, egressSpanId, destination, request, cancellationToken).ConfigureAwait(false);
        string? redirectRefusal = FindRedirectRefusal(response, destination);
        if (redirectRefusal is not null)
        {
            response.Dispose();
            await this.ThrowRefusalAsync(
                context, egressSpanId, redirectRefusal, cancellationToken).ConfigureAwait(false);
        }
        await this.EmitAsync(
            context, egressSpanId, ExecutionEventType.EgressCompleted, ExecutionStatus.Succeeded,
            $"{destination.Host} answered {(int)response.StatusCode}", cancellationToken)
            .ConfigureAwait(false);
        return response;
    }

    private async Task<HttpResponseMessage> SendNetworkAsync(
        EgressOperationContext context,
        Guid egressSpanId,
        Uri destination,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            HttpResponseMessage response = await base.SendAsync(
                request, cancellationToken).ConfigureAwait(false);
            await this.BoundResponseAsync(response, cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            await this.EmitAsync(
                context, egressSpanId, ExecutionEventType.EgressFailed, ExecutionStatus.Failed,
                $"{destination.Host} request failed", cancellationToken)
                .ConfigureAwait(false);
            throw;
        }
    }

    private async Task BoundResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        HttpContent original = response.Content;
        if (original.Headers.ContentLength > this.maxResponseBytes)
        {
            response.Dispose();
            throw new InvalidDataException(
                $"MCP response exceeds {this.maxResponseBytes} bytes.");
        }

        Stream body = await original.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var replacement = new StreamContent(
            new BoundedResponseReadStream(body, original, this.maxResponseBytes));
        foreach (KeyValuePair<string, IEnumerable<string>> header in original.Headers)
        {
            replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        response.Content = replacement;
    }

    private async Task EnsureAllowedAsync(
        EgressOperationContext context,
        Guid egressSpanId,
        Uri destination,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        string? refusal = context.Privacy != PrivacyClass.Egressable
            ? "MCP request bodies must be explicitly Egressable (D-012)."
            : this.FindDestinationRefusal(destination)
                ?? await this.FindBodyRefusalAsync(content, cancellationToken).ConfigureAwait(false)
                ?? await this.budget.FindRefusalAsync(cancellationToken).ConfigureAwait(false);
        if (refusal is null)
        {
            return;
        }

        await this.ThrowRefusalAsync(
            context, egressSpanId, refusal, cancellationToken).ConfigureAwait(false);
    }

    private async Task ThrowRefusalAsync(
        EgressOperationContext context,
        Guid egressSpanId,
        string refusal,
        CancellationToken cancellationToken)
    {
        await this.EmitAsync(
            context, egressSpanId, ExecutionEventType.EgressRefused, ExecutionStatus.Failed,
            refusal, cancellationToken).ConfigureAwait(false);
        this.logger.LogWarning("MCP egress refused: {Reason}", refusal);
        throw new EgressRefusedException(refusal);
    }

    private async Task<string?> FindBodyRefusalAsync(
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        if (content is null)
        {
            return null;
        }

        if (content.Headers.ContentLength > this.maxRequestBytes)
        {
            return $"MCP request body exceeds {this.maxRequestBytes} bytes.";
        }

        try
        {
            await content.LoadIntoBufferAsync(
                this.maxRequestBytes, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (HttpRequestException)
        {
            return $"MCP request body exceeds {this.maxRequestBytes} bytes.";
        }
    }

    private string? FindDestinationRefusal(Uri destination)
    {
        if (destination.Scheme != Uri.UriSchemeHttps)
        {
            return "Remote MCP egress requires HTTPS.";
        }

        var allowed = false;
        foreach (string allowedHost in this.allowedHosts)
        {
            if (string.Equals(allowedHost, destination.Host, StringComparison.OrdinalIgnoreCase))
            {
                allowed = true;
                break;
            }
        }

        if (!allowed)
        {
            return $"MCP host '{destination.Host}' is not on the egress allowlist.";
        }

        string uri = Uri.UnescapeDataString(destination.AbsoluteUri);
        foreach (string fragment in this.forbiddenFragments)
        {
            if (uri.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return $"The outgoing MCP URI contains forbidden fragment '{fragment}'.";
            }
        }

        return null;
    }

    private static string? FindRedirectRefusal(
        HttpResponseMessage response,
        Uri requestUri)
    {
        if ((int)response.StatusCode is < 300 or > 399
            || response.Headers.Location is not { } location)
        {
            return null;
        }

        Uri destination = location.IsAbsoluteUri ? location : new Uri(requestUri, location);
        bool sameOrigin = string.Equals(
                requestUri.Scheme, destination.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(requestUri.Host, destination.Host, StringComparison.OrdinalIgnoreCase)
            && requestUri.Port == destination.Port;
        return sameOrigin
            ? "MCP redirects are refused; configure the final server endpoint."
            : "Cross-origin MCP redirects are refused to protect credentials and session headers.";
    }

    private static void EnsureRedirectsDisabled(HttpMessageHandler innerHandler)
    {
        bool enabled = innerHandler switch
        {
            HttpClientHandler handler => handler.AllowAutoRedirect,
            SocketsHttpHandler handler => handler.AllowAutoRedirect,
            _ => false,
        };
        if (enabled)
        {
            throw new ArgumentException(
                "The MCP network handler must disable automatic redirects.", nameof(innerHandler));
        }
    }

    private static IReadOnlyList<string> SnapshotStrings(
        IList<string> values,
        string parameterName)
    {
        var snapshot = new string[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            string value = values[index];
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "MCP egress policy entries cannot be blank.", parameterName);
            }

            snapshot[index] = value;
        }

        return snapshot;
    }

    private Task<long> EmitAsync(
        EgressOperationContext context,
        Guid egressSpanId,
        ExecutionEventType type,
        ExecutionStatus status,
        string label,
        CancellationToken cancellationToken)
    {
        var executionEvent = new ExecutionEvent(
            Guid.NewGuid(), context.TraceId, egressSpanId, context.ParentSpanId,
            context.Origin, ACTOR, type, status, this.clock.GetUtcNow(), label);
        return this.eventStore.AppendAsync(executionEvent, cancellationToken);
    }
}
