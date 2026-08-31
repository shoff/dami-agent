using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Dami.Gui;

/// <summary>The Network tab: the collector's sweeps as a live dashboard.</summary>
/// <remarks>
/// Deterministic panels poll; the analysis panel does not. Sweeps land once a day, but
/// the runtime's own egress moves by the second, so the tab pairs a 20-second poll of
/// both with a model panel that runs only on load and on demand — an unattended model
/// call every 20 seconds would be cost without information.
///
/// The analysis goes through a normal local turn (<c>/turns</c>): traced, LocalOnly,
/// and rendered under a label that says it is the model speculating, because D-012
/// keeps network topology on this host and the UI shows evidence, not oracle claims.
/// </remarks>
public sealed partial class MainWindow
{
    private static readonly TimeSpan networkPollInterval = TimeSpan.FromSeconds(20);

    private Button networkAnalyze = null!;
    private string networkFactsJson = string.Empty;

    private void InitializeNetwork()
    {
        this.networkAnalyze = Require<Button>(this, "NetworkAnalyze");
        this.networkAnalyze.Click += this.OnNetworkAnalyze;
        _ = this.FollowNetworkAsync();
    }

    private void OnNetworkAnalyze(object? sender, RoutedEventArgs e)
    {
        _ = this.AnalyzeNetworkAsync();
    }

    private async Task FollowNetworkAsync()
    {
        var analyzed = false;
        while (!this.lifetime.IsCancellationRequested)
        {
            var loaded = await this.RefreshNetworkAsync().ConfigureAwait(true);
            if (loaded && !analyzed)
            {
                analyzed = true;
                _ = this.AnalyzeNetworkAsync();
            }

            try
            {
                await Task.Delay(networkPollInterval, this.lifetime.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<bool> RefreshNetworkAsync()
    {
        using var facts = await this.runtime
            .GetAsync("/domains/network", this.lifetime.Token).ConfigureAwait(true);
        await this.RefreshNetworkEgressAsync().ConfigureAwait(true);
        if (facts is null)
        {
            this.state.NetworkMessage = "The runtime did not answer /domains/network.";
            return false;
        }

        var root = facts.RootElement;
        this.networkFactsJson = root.GetRawText();
        Replace(this.state.NetworkTiles, NetworkActivity.Tiles(root));
        Replace(this.state.NetworkLatest, NetworkActivity.Latest(root));
        Replace(this.state.NetworkChanges, NetworkActivity.Changes(root));
        ReplaceSeries(this.state.NetworkProblemChart, FitnessCharts.Trend(
            "problems", NetworkActivity.ProblemsBySweep(root), "#E0604F", "faults"));
        this.state.NetworkMessage = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"polling every {networkPollInterval.TotalSeconds:0} s · sweeps land daily · egress chart is the last 30 min");
        return root.GetArrayLength() > 0;
    }

    /// <summary>The runtime's outbound calls, bucketed by the runtime's own clock.</summary>
    private async Task RefreshNetworkEgressAsync()
    {
        using var activity = await this.runtime
            .GetAsync("/activity?minutes=30&buckets=60", this.lifetime.Token).ConfigureAwait(true);
        if (activity is null)
        {
            return;
        }

        var counts = new Dictionary<string, IReadOnlyList<int>>();
        foreach (var series in activity.RootElement.GetProperty("series").EnumerateArray())
        {
            if (series.GetProperty("name").GetString() == "egress")
            {
                counts["egress"] = series.GetProperty("values")
                    .EnumerateArray().Select(value => value.GetInt32()).ToList();
            }
        }

        Reconcile.Sync(this.state.NetworkEgress, ActivityChart.Build(counts));
    }

    private async Task AnalyzeNetworkAsync()
    {
        if (this.networkFactsJson.Length == 0)
        {
            return;
        }

        this.networkAnalyze.IsEnabled = false;
        this.state.NetworkAnalysis = "the local model is reading the sweeps…";
        try
        {
            using var facts = JsonDocument.Parse(this.networkFactsJson);
            using var reply = await this.runtime.PostAsync(
                "/turns",
                new { message = NetworkActivity.AnalysisPrompt(facts.RootElement) },
                this.lifetime.Token).ConfigureAwait(true);

            this.state.NetworkAnalysis =
                reply?.RootElement.TryGetProperty("answer", out var answer) is true
                    ? answer.GetString() ?? "(the runtime returned nothing)"
                    : "the runtime could not analyze the sweeps";
        }
        finally
        {
            this.networkAnalyze.IsEnabled = true;
        }
    }
}
