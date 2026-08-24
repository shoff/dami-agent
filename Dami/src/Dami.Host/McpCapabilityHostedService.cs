using Dami.Capabilities.Mcp;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Privacy;
using Dami.Privacy;
using System.Runtime.ExceptionServices;

namespace Dami.Host;

/// <summary>Discovers configured MCP tools and owns their connections with the Host.</summary>
public sealed class McpCapabilityHostedService : IHostedService, IAsyncDisposable
{
    private const string ACTOR = "mcp-host";

    private readonly IReadOnlyList<McpServerRegistration> registrations;
    private readonly McpCapabilityLoader loader;
    private readonly McpEgressHttpMessageHandler egressHandler;
    private readonly IEgressOperationScopeFactory scopeFactory;
    private readonly IExecutionEventStore eventStore;
    private readonly TimeProvider clock;
    private readonly List<McpServerConnection> connections = [];
    private int disposed;

    /// <summary>Creates the Host-owned MCP lifecycle.</summary>
    public McpCapabilityHostedService(
        IReadOnlyList<McpServerRegistration> registrations,
        McpCapabilityLoader loader,
        McpEgressHttpMessageHandler egressHandler,
        IEgressOperationScopeFactory scopeFactory,
        IExecutionEventStore eventStore,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(egressHandler);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(clock);
        this.registrations = registrations;
        this.loader = loader;
        this.egressHandler = egressHandler;
        this.scopeFactory = scopeFactory;
        this.eventStore = eventStore;
        this.clock = clock;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (this.registrations.Count == 0)
        {
            return Task.CompletedTask;
        }

        return this.RunStartupAsync(cancellationToken);
    }

    private async Task RunStartupAsync(CancellationToken cancellationToken)
    {
        var traceId = Guid.NewGuid();
        var rootSpanId = Guid.NewGuid();
        await this.EmitAsync(
            traceId, rootSpanId, ExecutionEventType.TraceStarted,
            ExecutionStatus.Running, cancellationToken).ConfigureAwait(false);
        try
        {
            await this.LoadAllAsync(traceId, rootSpanId, cancellationToken).ConfigureAwait(false);
            await this.EmitAsync(
                traceId, rootSpanId, ExecutionEventType.TraceCompleted,
                ExecutionStatus.Succeeded, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await this.EmitAsync(
                traceId, rootSpanId, ExecutionEventType.TraceCancelled,
                ExecutionStatus.Cancelled, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception)
        {
            await this.EmitAsync(
                traceId, rootSpanId, ExecutionEventType.TraceFailed,
                ExecutionStatus.Failed, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return this.ShutdownAsync();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => new(this.ShutdownAsync());

    private async Task LoadAllAsync(
        Guid traceId,
        Guid rootSpanId,
        CancellationToken cancellationToken)
    {
        foreach (McpServerRegistration registration in this.registrations)
        {
            EgressOperationContext connect = CreateContext(
                "connect configured MCP server", traceId, rootSpanId);
            McpServerConnection connection = await this.ConnectAsync(
                registration, connect, cancellationToken).ConfigureAwait(false);
            try
            {
                await this.loader.LoadAsync(
                    registration, connection, this.clock.GetUtcNow(),
                    CreateContext("discover configured MCP tools", traceId, rootSpanId),
                    cancellationToken).ConfigureAwait(false);
                this.connections.Add(connection);
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    private Task<McpServerConnection> ConnectAsync(
        McpServerRegistration registration,
        EgressOperationContext context,
        CancellationToken cancellationToken)
    {
        return registration.Endpoint.IsLoopback
            ? McpServerConnection.ConnectAsync(registration, cancellationToken)
            : McpServerConnection.ConnectRemoteAsync(
                registration, this.egressHandler, this.scopeFactory, context, cancellationToken);
    }

    private Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0 || this.connections.Count == 0)
        {
            return Task.CompletedTask;
        }

        return this.RunShutdownAsync();
    }

    private async Task RunShutdownAsync()
    {
        var traceId = Guid.NewGuid();
        var rootSpanId = Guid.NewGuid();
        await this.EmitAsync(
            traceId, rootSpanId, ExecutionEventType.TraceStarted,
            ExecutionStatus.Running, CancellationToken.None).ConfigureAwait(false);
        try
        {
            await this.DisposeConnectionsAsync(traceId, rootSpanId).ConfigureAwait(false);
            await this.EmitAsync(
                traceId, rootSpanId, ExecutionEventType.TraceCompleted,
                ExecutionStatus.Succeeded, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await this.EmitAsync(
                traceId, rootSpanId, ExecutionEventType.TraceFailed,
                ExecutionStatus.Failed, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task DisposeConnectionsAsync(Guid traceId, Guid rootSpanId)
    {
        Exception? firstFailure = null;
        for (var index = this.connections.Count - 1; index >= 0; index--)
        {
            try
            {
                await this.connections[index].DisposeAsync(CreateContext(
                    "close configured MCP server", traceId, rootSpanId)).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                firstFailure ??= exception;
            }
        }

        this.connections.Clear();
        if (firstFailure is not null)
        {
            ExceptionDispatchInfo.Capture(firstFailure).Throw();
        }
    }

    private static EgressOperationContext CreateContext(
        string purpose,
        Guid traceId,
        Guid rootSpanId)
    {
        return new EgressOperationContext(
            purpose, PrivacyClass.Egressable, traceId, rootSpanId,
            ExecutionOrigin.ScheduledService);
    }

    private Task<long> EmitAsync(
        Guid traceId,
        Guid spanId,
        ExecutionEventType type,
        ExecutionStatus status,
        CancellationToken cancellationToken)
    {
        var executionEvent = new ExecutionEvent(
            Guid.NewGuid(), traceId, spanId, null, ExecutionOrigin.ScheduledService,
            ACTOR, type, status, this.clock.GetUtcNow(), "load configured MCP capabilities");
        return this.eventStore.AppendAsync(executionEvent, cancellationToken);
    }
}
