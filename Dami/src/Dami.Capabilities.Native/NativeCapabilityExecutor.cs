using Dami.Contracts.Capabilities;

namespace Dami.Capabilities.Native;

/// <summary>Dispatches native handlers under a cooperative execution timeout.</summary>
public sealed class NativeCapabilityExecutor : ICapabilityExecutionSource
{
    private readonly INativeCapabilityCatalog catalog;
    private readonly TimeSpan executionTimeout;

    /// <summary>Creates the native executor.</summary>
    public NativeCapabilityExecutor(
        INativeCapabilityCatalog catalog,
        NativeCapabilityExecutorOptions options)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(options);
        if (options.ExecutionTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), options.ExecutionTimeout, "Execution timeout must be positive.");
        }

        this.catalog = catalog;
        this.executionTimeout = options.ExecutionTimeout;
    }

    /// <inheritdoc />
    public bool Owns(Guid capabilityId) => this.catalog.Find(capabilityId) is not null;

    /// <inheritdoc />
    public async Task<CapabilityExecutionResult> ExecuteAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var invocation = request.Invocation;
        INativeCapabilityHandler handler = this.catalog.Find(invocation.CapabilityId)
            ?? throw new KeyNotFoundException(
                $"Native capability '{invocation.CapabilityId}' is not registered.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(this.executionTimeout);

        try
        {
            var execution = handler.ExecuteAsync(
                request,
                timeout.Token);
            return await execution
                .WaitAsync(this.executionTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw CreateTimeoutException(invocation.CapabilityId, this.executionTimeout, exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw CreateTimeoutException(invocation.CapabilityId, this.executionTimeout, exception);
        }
    }

    private static TimeoutException CreateTimeoutException(
        Guid capabilityId,
        TimeSpan executionTimeout,
        Exception innerException)
    {
        return new TimeoutException(
            $"Native capability '{capabilityId}' exceeded {executionTimeout}.",
            innerException);
    }
}
