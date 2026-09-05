using Dami.Gateway.Discord;
using Microsoft.Extensions.Logging;

namespace Dami.Host.Discord;

/// <summary>Keeps Discord's expiring typing indicator alive for one turn.</summary>
public sealed class DiscordTypingIndicator
{
    private readonly IDiscordRest rest;
    private readonly DiscordOptions options;
    private readonly ILogger<DiscordTypingIndicator> logger;

    /// <summary>Creates the indicator.</summary>
    public DiscordTypingIndicator(
        IDiscordRest rest,
        DiscordOptions options,
        ILogger<DiscordTypingIndicator> logger)
    {
        ArgumentNullException.ThrowIfNull(rest);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        this.rest = rest;
        this.options = options;
        this.logger = logger;
    }

    /// <summary>Starts immediately and refreshes until the returned lease is disposed.</summary>
    public async Task<IAsyncDisposable> BeginAsync(
        string conversationId, CancellationToken cancellationToken)
    {
        await this.TryPostAsync(conversationId, cancellationToken).ConfigureAwait(false);
        var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        return new Lease(stop, this.RefreshAsync(conversationId, stop.Token));
    }

    private async Task RefreshAsync(string conversationId, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(this.options.TypingRefresh, cancellationToken).ConfigureAwait(false);
                await this.TryPostAsync(conversationId, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task TryPostAsync(string conversationId, CancellationToken cancellationToken)
    {
        try
        {
            await this.rest.PostTypingAsync(conversationId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.logger.LogDebug(exception, "Discord typing indicator failed");
        }
    }

    private sealed class Lease : IAsyncDisposable
    {
        private readonly CancellationTokenSource stop;
        private readonly Task refresh;

        public Lease(CancellationTokenSource stop, Task refresh)
        {
            this.stop = stop;
            this.refresh = refresh;
        }

        public async ValueTask DisposeAsync()
        {
            await this.stop.CancelAsync().ConfigureAwait(false);
            this.stop.Dispose();
        }
    }
}
