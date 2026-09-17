using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Dami.Contracts.Research;

namespace Dami.Gui;

public sealed partial class ResearchWorkspace
{
    private static readonly JsonSerializerOptions researchJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
    private RuntimeClient? runtime;
    private CancellationToken lifetimeToken;
    private bool refreshing;
    private IReadOnlyList<ResearchRunSummary> lastHistory = [];
    private HashSet<Guid>? beforeLaunch;

    /// <summary>Connects the page to the authenticated runtime and the existing chat submission.</summary>
    public void Initialize(RuntimeClient client, Func<string, string, ResearchLaunchResult> launch, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(launch);
        this.runtime = client;
        this.lifetimeToken = token;
        this.SelectionRequested += (_, _) => _ = this.LoadSelectedAsync();
        this.Control<Button>("RefreshResearch").Click += (_, _) => _ = this.RefreshAsync();
        this.Control<Button>("StartResearch").Click += (_, _) =>
        {
            var result = launch(this.Control<TextBox>("ResearchSeed").Text ?? "",
                this.Control<TextBox>("ResearchQuestion").Text ?? "");
            this.Control<TextBlock>("LaunchMessage").Text = result.Message;
            if (result.Prompt is not null)
            {
                this.Control<Border>("ResearchForm").IsVisible = false;
                this.beforeLaunch = this.lastHistory.Select(run => run.RunId).ToHashSet();
                this.Control<ComboBox>("ResearchFilter").SelectedIndex = 0;
                this.Control<TextBox>("ResearchSearch").Text = "";
            }
        };
    }

    /// <summary>Polls lightweight history, then only the selected artifact's changed revision.</summary>
    public async Task RefreshAsync()
    {
        if (this.runtime is null || this.refreshing || this.lifetimeToken.IsCancellationRequested)
        {
            return;
        }

        this.refreshing = true;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(this.lifetimeToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var json = await this.runtime.GetAsync("/research/runs?limit=100", timeout.Token).ConfigureAwait(true);
            if (json?.RootElement.ValueKind != JsonValueKind.Array)
            {
                this.Control<TextBlock>("ResearchSync").Text = "Research history unavailable. Any displayed results may be stale.";
                return;
            }

            this.ApplyHistory(json.RootElement.Deserialize<ResearchRunSummary[]>(researchJson) ?? []);
            this.Control<TextBlock>("ResearchSync").Text = $"Updated {TimeProvider.System.GetLocalNow():h:mm:ss tt} · newest 100 runs";
            await this.LoadSelectedAsync().ConfigureAwait(true);
        }
        catch (Exception exception) { this.Control<TextBlock>("ResearchSync").Text = $"History unavailable: {exception.Message}"; }
        finally { this.refreshing = false; }
    }

    private void ApplyHistory(IReadOnlyList<ResearchRunSummary> runs)
    {
        this.lastHistory = runs;
        this.PresentHistory(runs);
        if (this.beforeLaunch is { } previous && runs.FirstOrDefault(run => !previous.Contains(run.RunId)) is { } added)
        {
            this.beforeLaunch = null;
            this.history.Select(added.RunId);
            this.RenderHistory();
            this.Control<TextBlock>("LaunchMessage").Text = "Research is collecting sources. This run is saved as it progresses.";
        }
    }

    private async Task LoadSelectedAsync()
    {
        if (this.runtime is null || this.history.Selected is not { } summary
            || (this.selectedRun?.RunId == summary.RunId && this.selectedRun.Revision >= summary.Revision))
        {
            return;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(this.lifetimeToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var json = await this.runtime.GetAsync($"/research/runs/{summary.RunId:D}", timeout.Token).ConfigureAwait(true);
            var run = json?.RootElement.ValueKind == JsonValueKind.Object
                ? json.RootElement.Deserialize<ResearchRun>(researchJson) : null;
            if (run is null)
            {
                this.Control<TextBlock>("ResearchSync").Text = "The selected report could not be loaded. Refresh to retry.";
                return;
            }

            this.ShowRun(run);
        }
        catch (Exception exception) { this.Control<TextBlock>("ResearchSync").Text = $"Report unavailable: {exception.Message}"; }
    }
}
