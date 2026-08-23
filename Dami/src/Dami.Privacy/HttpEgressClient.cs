using Dami.Contracts.Events;
using Dami.Contracts.Privacy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Privacy;

/// <summary>The one implementation of the egress boundary.</summary>
/// <remarks>
/// Every send — allowed or refused — is a durable execution event in the caller's trace,
/// so "what has left this machine" is a database query rather than a hope. A refusal is
/// an exception, not a soft failure: code that quietly degrades when the boundary blocks
/// it would hide exactly the drift D-012 warns about.
/// </remarks>
public sealed class HttpEgressClient : IEgressClient
{
    private const string ACTOR = "egress";
    private const int MAX_REDIRECTS = 5;

    private readonly HttpClient httpClient;
    private readonly IEgressBudget egressBudget;
    private readonly EgressOptions egressOptions;
    private readonly IExecutionEventStore eventStore;
    private readonly TimeProvider clock;
    private readonly ILogger<HttpEgressClient> logger;

    /// <summary>Creates the client.</summary>
    public HttpEgressClient(
        HttpClient httpClient,
        IEgressBudget egressBudget,
        IOptions<EgressOptions> egressOptions,
        IExecutionEventStore eventStore,
        TimeProvider clock,
        ILogger<HttpEgressClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(egressBudget);
        ArgumentNullException.ThrowIfNull(egressOptions);
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.httpClient = httpClient;
        this.egressBudget = egressBudget;
        this.egressOptions = egressOptions.Value;

        if (this.egressOptions.MaxResponseBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(egressOptions),
                this.egressOptions.MaxResponseBytes,
                "Egress response limit must be positive.");
        }

        this.eventStore = eventStore;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<EgressResponse> SendAsync(EgressRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await this.EmitAsync(
            request, ExecutionEventType.EgressRequested, ExecutionStatus.Running,
            $"{request.Purpose} -> {request.Destination.Host}", cancellationToken).ConfigureAwait(false);

        await this.ThrowIfOverBudgetAsync(request, cancellationToken).ConfigureAwait(false);

        try
        {
            return await this.SendAllowedAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            await this.EmitAsync(
                request,
                ExecutionEventType.EgressFailed,
                ExecutionStatus.Failed,
                $"{request.Destination.Host} failed: {exception.Message}",
                cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<EgressResponse> SendAllowedAsync(
        EgressRequest request,
        CancellationToken cancellationToken)
    {
        var destination = request.Destination;

        for (var redirects = 0; redirects <= MAX_REDIRECTS; redirects++)
        {
            await this.ThrowIfRefusedAsync(request, destination, cancellationToken).ConfigureAwait(false);
            using var response = await this.httpClient
                .GetAsync(destination, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (TryGetRedirect(response, destination, out var redirected))
            {
                destination = redirected;
                continue;
            }

            return await this.CompleteAsync(request, destination, response, cancellationToken)
                .ConfigureAwait(false);
        }

        throw new HttpRequestException($"Egress exceeded the redirect limit of {MAX_REDIRECTS}.");
    }

    private async Task ThrowIfRefusedAsync(
        EgressRequest request,
        Uri destination,
        CancellationToken cancellationToken)
    {
        var refusal = this.FindRefusal(destination);
        if (refusal is not null)
        {
            await this.EmitAsync(
                request, ExecutionEventType.EgressRefused, ExecutionStatus.Failed,
                refusal, cancellationToken).ConfigureAwait(false);

            this.logger.LogWarning("Egress refused: {Reason}", refusal);
            throw new EgressRefusedException(refusal);
        }
    }

    private async Task<EgressResponse> CompleteAsync(
        EgressRequest request,
        Uri destination,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await this.EnsureResponseSizeAsync(response.Content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        await this.EmitAsync(
            request, ExecutionEventType.EgressCompleted, ExecutionStatus.Succeeded,
            $"{destination.Host} answered {(int)response.StatusCode}", cancellationToken)
            .ConfigureAwait(false);

        return new EgressResponse((int)response.StatusCode, body);
    }

    private async Task EnsureResponseSizeAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        var length = content.Headers.ContentLength;
        if (length > this.egressOptions.MaxResponseBytes)
        {
            throw new InvalidDataException(
                $"Egress response is {length} bytes; limit is {this.egressOptions.MaxResponseBytes}.");
        }

        await content
            .LoadIntoBufferAsync(this.egressOptions.MaxResponseBytes, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ThrowIfOverBudgetAsync(
        EgressRequest request,
        CancellationToken cancellationToken)
    {
        var refusal = await this.egressBudget.FindRefusalAsync(cancellationToken).ConfigureAwait(false);
        if (refusal is not null)
        {
            await this.EmitAsync(
                request, ExecutionEventType.EgressRefused, ExecutionStatus.Failed,
                refusal, cancellationToken).ConfigureAwait(false);

            this.logger.LogWarning("Egress refused: {Reason}", refusal);
            throw new EgressRefusedException(refusal);
        }
    }

    private string? FindRefusal(Uri destination)
    {
        if (destination.Scheme != Uri.UriSchemeHttps)
        {
            return $"scheme '{destination.Scheme}' is not allowed; outbound egress requires HTTPS";
        }

        var allowed = this.egressOptions.AllowedHosts
            .Any(host => string.Equals(host, destination.Host, StringComparison.OrdinalIgnoreCase));

        if (!allowed)
        {
            return $"host '{destination.Host}' is not on the egress allowlist";
        }

        var uri = Uri.UnescapeDataString(destination.AbsoluteUri);
        var leaked = this.egressOptions.ForbiddenFragments
            .FirstOrDefault(fragment => uri.Contains(fragment, StringComparison.OrdinalIgnoreCase));

        return leaked is null
            ? null
            : $"the outgoing URI contains the forbidden fragment '{leaked}'";
    }

    private static bool TryGetRedirect(
        HttpResponseMessage response,
        Uri requestUri,
        out Uri redirected)
    {
        if ((int)response.StatusCode is >= 300 and <= 399 && response.Headers.Location is { } location)
        {
            redirected = location.IsAbsoluteUri ? location : new Uri(requestUri, location);
            return true;
        }

        redirected = requestUri;
        return false;
    }

    private Task<long> EmitAsync(
        EgressRequest request,
        ExecutionEventType type,
        ExecutionStatus status,
        string label,
        CancellationToken cancellationToken)
    {
        var executionEvent = new ExecutionEvent(
            eventId: Guid.NewGuid(),
            traceId: request.TraceId,
            spanId: Guid.NewGuid(),
            parentSpanId: null,
            origin: request.Origin,
            actorId: ACTOR,
            type: type,
            status: status,
            occurredAt: this.clock.GetUtcNow(),
            label: label);

        return this.eventStore.AppendAsync(executionEvent, cancellationToken);
    }
}
