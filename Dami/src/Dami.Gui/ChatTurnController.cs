namespace Dami.Gui;

/// <summary>Owns the lifetime of the desktop's current conversation request.</summary>
/// <remarks>Called on the UI thread; cancellation keeps the request active until it returns.</remarks>
public sealed class ChatTurnController
{
    private CancellationTokenSource? active;

    /// <summary>Whether a request is still running or stopping.</summary>
    public bool IsRunning => this.active is not null;

    /// <summary>Requests cancellation of the active send.</summary>
    public void Stop() => this.active?.Cancel();

    /// <summary>Runs a send operation from either the keyboard or the Send button.</summary>
    public async Task RunAsync(Func<CancellationToken, Task> send, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(send);
        if (this.IsRunning)
        {
            return;
        }

        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        this.active = request;
        try
        {
            await send(request.Token).ConfigureAwait(true);
        }
        finally
        {
            this.active = null;
        }
    }
}
