using System.Globalization;

namespace Dami.Gui;

/// <summary>Live operational visibility from the canonical durable event ledger.</summary>
public sealed partial class MainWindow
{
    private const int OBSERVABILITY_WINDOW = 200;

    private async Task RefreshObservabilityAsync()
    {
        using var events = await this.runtime.GetAsync(
            $"/events/recent?limit={OBSERVABILITY_WINDOW}", this.lifetime.Token).ConfigureAwait(true);
        if (events is null)
        {
            this.state.ObservabilityMessage = "recent event feed unavailable · dami-host may need redeploying";
            return;
        }

        var snapshot = ObservabilityFeed.Build(events.RootElement);
        Reconcile.Sync(this.state.ObservabilityTiles, snapshot.Tiles);
        Reconcile.Sync(this.state.ObservabilityEvents, snapshot.Events);
        var refreshed = TimeProvider.System.GetLocalNow()
            .ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        this.state.ObservabilityMessage =
            $"live · newest {snapshot.Events.Count} durable events · refreshed {refreshed}";
    }
}
