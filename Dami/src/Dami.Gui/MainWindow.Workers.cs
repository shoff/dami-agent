using System.Text.Json;
using Avalonia.Controls;

namespace Dami.Gui;

/// <summary>What the proactive tier has been doing while Steve was not looking.</summary>
/// <remarks>
/// The tier is the part of Dami that runs unattended, and until this view existed there
/// was no way to see it from any interface: three of its eleven services had not run since
/// 2026-08-23 and nothing said whether that was their cadence or a fault.
///
/// The useful unit is not "a pass ran" — it is what the pass did, and that is a trace. A
/// scout pass replays as the feeds it fetched, what each host answered (including the 429
/// that cost it half its sources), and every item it surfaced. So the view is service →
/// pass → the durable event stream for that pass, read back from the runtime rather than
/// summarised here. Read-only throughout: starting a pass is an operator's act.
/// </remarks>
public sealed partial class MainWindow
{
    private static readonly TimeSpan workerPollInterval = TimeSpan.FromSeconds(30);

    private ListBox workerList = null!;
    private ListBox workerRunList = null!;

    private void InitialiseWorkers()
    {
        this.workerList = Require<ListBox>(this, "WorkerList");
        this.workerRunList = Require<ListBox>(this, "WorkerRunList");
        this.workerList.SelectionChanged += this.OnWorkerSelected;
        this.workerRunList.SelectionChanged += this.OnWorkerRunSelected;
        _ = this.FollowWorkersAsync();
    }

    private void OnWorkerSelected(object? sender, SelectionChangedEventArgs e)
    {
        var runs = (this.workerList.SelectedItem as WorkerRow)?.Recent ?? [];
        Reconcile.Sync(this.state.SelectedWorkerRuns, runs);
        this.state.WorkerTrace.Clear();
        this.state.WorkerTraceMessage = string.Empty;
    }

    private void OnWorkerRunSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (this.workerRunList.SelectedItem is WorkerRun run)
        {
            _ = this.LoadWorkerTraceAsync(run);
        }
    }

    private async Task LoadWorkerTraceAsync(WorkerRun run)
    {
        this.state.WorkerTraceMessage = $"replaying trace {run.Trace}…";
        using var events = await this.runtime
            .GetAsync($"/traces/{run.TraceId:D}", this.lifetime.Token).ConfigureAwait(true);
        if (events is null)
        {
            this.state.WorkerTrace.Clear();
            this.state.PassSummary = PassSummary.none;
            this.state.WorkerTraceMessage = $"trace {run.Trace} could not be read";
            return;
        }

        var raw = events.RootElement.EnumerateArray().ToList();
        Reconcile.Sync(this.state.WorkerTrace, Replay(raw));
        this.state.PassSummary = Summarise(this.state.WorkerTrace);
        this.state.WorkerTraceMessage = $"trace {run.Trace}";
    }

    /// <summary>Turns the raw event stream into a waterfall.</summary>
    /// <remarks>
    /// The bar is the gap since the previous event, scaled against the longest gap in the
    /// pass, so the slow step is the wide one. A bar of elapsed-since-start would grow
    /// monotonically and say nothing.
    /// </remarks>
    private static List<PassEvent> Replay(List<JsonElement> raw)
    {
        if (raw.Count == 0)
        {
            return [];
        }

        var times = raw.Select(item => item.GetProperty("occurredAt").GetDateTimeOffset()).ToList();
        var start = times[0];
        var gaps = times.Select((at, index) =>
            index == 0 ? TimeSpan.Zero : at - times[index - 1]).ToList();
        var widest = gaps.Max(gap => gap.TotalSeconds);

        return raw.Select((item, index) =>
        {
            var label = item.GetProperty("label").GetString() ?? string.Empty;
            var type = item.GetProperty("type").GetString() ?? string.Empty;
            var status = item.GetProperty("status").GetString() ?? string.Empty;
            return new PassEvent(
                times[index].ToLocalTime().ToString("HH:mm:ss"),
                index == 0 ? "start" : $"+{(times[index] - start).TotalSeconds:0.0}s",
                type,
                label,
                status,
                widest <= 0 ? 2 : Math.Max(2, gaps[index].TotalSeconds / widest * MAX_BAR),
                IsAlert(type, status, label));
        }).ToList();
    }

    private const double MAX_BAR = 120;

    /// <remarks>
    /// A non-2xx answer is the case this view exists for: the scout's second feed came
    /// back 429 and lost half its sources, and every surface in the system reported the
    /// pass as Completed. Status alone would never have shown it.
    /// </remarks>
    private static bool IsAlert(string type, string status, string label)
    {
        if (status is "Failed" or "Cancelled")
        {
            return true;
        }

        var answered = label.IndexOf("answered ", StringComparison.Ordinal);
        return answered >= 0
            && int.TryParse(label.AsSpan(answered + 9).Trim(), out var code)
            && code is < 200 or >= 300;
    }

    private static PassSummary Summarise(IReadOnlyCollection<PassEvent> pass)
    {
        if (pass.Count == 0)
        {
            return PassSummary.none;
        }

        var last = pass.Last().Offset;
        return new PassSummary(
            last == "start" ? "instant" : last.TrimStart('+'),
            pass.Count(item => item.Type == "EgressRequested"),
            pass.Count(item => item.Type is "Surfaced" or "Concluded" or "Observed" or "FactRecorded"),
            pass.Count(item => item.IsAlert));
    }

    private async Task FollowWorkersAsync()
    {
        while (!this.lifetime.IsCancellationRequested)
        {
            await this.RefreshWorkersAsync().ConfigureAwait(true);
            try
            {
                await Task.Delay(workerPollInterval, this.lifetime.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RefreshWorkersAsync()
    {
        using var services = await this.runtime
            .GetAsync("/proactive?recent=25", this.lifetime.Token).ConfigureAwait(true);
        if (services is null)
        {
            return;
        }

        var current = services.RootElement.EnumerateArray().Select(Describe).ToList();
        Reconcile.Sync(this.state.Workers, current);
    }

    /// <remarks>
    /// The staleness judgement is the server's, from one clock. A panel recomputing it
    /// locally would disagree with the runtime the moment the two clocks did — which on
    /// this host they have.
    /// </remarks>
    private static WorkerRow Describe(JsonElement service)
    {
        var name = service.GetProperty("serviceName").GetString() ?? string.Empty;
        var status = service.GetProperty("lastStatus").GetString() ?? string.Empty;
        var hours = service.GetProperty("sinceLastRunHours").GetDouble();
        var runs = service.GetProperty("runs").GetInt32();
        var recent = service.GetProperty("recent").EnumerateArray()
            .Select(run =>
            {
                var traceId = run.GetProperty("traceId").GetGuid();
                return new WorkerRun(
                    run.GetProperty("ranAt").GetDateTimeOffset().ToLocalTime(),
                    run.GetProperty("status").GetString() ?? string.Empty,
                    traceId.ToString("N")[..8],
                    traceId);
            })
            .ToList();

        return new WorkerRow(name, status, Age(hours), runs, recent);
    }

    private static string Age(double hours)
    {
        return hours switch
        {
            < 1 => "just now",
            < 48 => $"{hours:0} h ago",
            _ => $"{hours / 24:0} days ago",
        };
    }
}
