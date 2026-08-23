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

    private readonly HttpClient httpClient;
    private readonly EgressOptions egressOptions;
    private readonly IExecutionEventStore eventStore;
    private readonly TimeProvider clock;
    private readonly ILogger<HttpEgressClient> logger;

    /// <summary>Creates the client.</summary>
    public HttpEgressClient(
        HttpClient httpClient,
        IOptions<EgressOptions> egressOptions,
        IExecutionEventStore eventStore,
        TimeProvider clock,
        ILogger<HttpEgressClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(egressOptions);
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.httpClient = httpClient;
        this.egressOptions = egressOptions.Value;
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

        var refusal = this.FindRefusal(request);
        if (refusal is not null)
        {
            await this.EmitAsync(
                request, ExecutionEventType.EgressRefused, ExecutionStatus.Failed,
                refusal, cancellationToken).ConfigureAwait(false);

            this.logger.LogWarning("Egress refused: {Reason}", refusal);
            throw new EgressRefusedException(refusal);
        }

        var response = await this.httpClient
            .GetAsync(request.Destination, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        await this.EmitAsync(
            request, ExecutionEventType.EgressCompleted, ExecutionStatus.Succeeded,
            $"{request.Destination.Host} answered {(int)response.StatusCode}", cancellationToken)
            .ConfigureAwait(false);

        return new EgressResponse((int)response.StatusCode, body);
    }

    private string? FindRefusal(EgressRequest request)
    {
        var allowed = this.egressOptions.AllowedHosts
            .Any(host => string.Equals(host, request.Destination.Host, StringComparison.OrdinalIgnoreCase));

        if (!allowed)
        {
            return $"host '{request.Destination.Host}' is not on the egress allowlist";
        }

        var uri = request.Destination.AbsoluteUri;
        var leaked = this.egressOptions.ForbiddenFragments
            .FirstOrDefault(fragment => uri.Contains(fragment, StringComparison.OrdinalIgnoreCase));

        return leaked is null
            ? null
            : $"the outgoing URI contains the forbidden fragment '{leaked}'";
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
