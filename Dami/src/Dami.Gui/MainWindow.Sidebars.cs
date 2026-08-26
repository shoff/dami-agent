
namespace Dami.Gui;

/// <summary>What Dami wants Steve to notice, and what it currently believes.</summary>
public sealed partial class MainWindow
{
    private async Task RefreshSidebarsAsync()
    {
        this.state.Attention.Clear();
        await this.AddApprovalsAsync().ConfigureAwait(true);
        await this.AddSurfacingsAsync().ConfigureAwait(true);
        await this.AddTodayAsync().ConfigureAwait(true);
        await this.RefreshBeliefsAsync().ConfigureAwait(true);
    }

    /// <summary>The board's questions for Steve, the civic week, and network problems (K4).</summary>
    private async Task AddTodayAsync()
    {
        using var boards = await this.runtime.GetAsync("/task-boards", this.lifetime.Token).ConfigureAwait(true);
        foreach (var board in boards?.RootElement.EnumerateArray() ?? default)
        {
            using var snapshot = await this.runtime
                .GetAsync($"/task-boards/{board.GetProperty("boardId").GetGuid():D}", this.lifetime.Token)
                .ConfigureAwait(true);
            if (snapshot is not null)
            {
                this.AddAll(TodayDigest.BoardQuestions(snapshot.RootElement.GetProperty("tasks")));
            }
        }

        using var civic = await this.runtime.GetAsync("/domains/civic", this.lifetime.Token).ConfigureAwait(true);
        if (civic is not null)
        {
            this.AddAll(TodayDigest.CivicWeek(civic.RootElement, DateOnly.FromDateTime(DateTime.Today)));
        }

        using var network = await this.runtime.GetAsync("/domains/network", this.lifetime.Token).ConfigureAwait(true);
        if (network is not null)
        {
            this.AddAll(TodayDigest.NetworkProblems(network.RootElement));
        }
    }

    private void AddAll(IEnumerable<SidebarItem> items)
    {
        foreach (var item in items)
        {
            this.state.Attention.Add(item);
        }
    }

    private async Task AddApprovalsAsync()
    {
        using var approvals = await this.runtime
            .GetAsync("/approvals", this.lifetime.Token).ConfigureAwait(true);
        if (approvals is null)
        {
            return;
        }

        foreach (var item in approvals.RootElement.EnumerateArray())
        {
            var id = item.GetProperty("approvalId").GetGuid().ToString("N")[..8];
            this.state.Attention.Add(new SidebarItem(
                id,
                "APPROVAL · " + (item.GetProperty("action").GetString() ?? string.Empty),
                $"{id} · {item.GetProperty("requestedBy").GetString()} · "
                + $"scope {item.GetProperty("scope").GetString()} · dami approve {id}"));
        }
    }

    private async Task AddSurfacingsAsync()
    {
        using var surfacings = await this.runtime
            .GetAsync("/surfacings", this.lifetime.Token).ConfigureAwait(true);
        if (surfacings is null)
        {
            return;
        }

        foreach (var item in surfacings.RootElement.EnumerateArray())
        {
            var id = item.GetProperty("surfacingId").GetGuid().ToString("N")[..8];
            this.state.Attention.Add(new SidebarItem(
                id,
                item.GetProperty("title").GetString() ?? string.Empty,
                $"{id} · {item.GetProperty("serviceName").GetString()} · "
                + $"confidence {item.GetProperty("confidence").GetDouble():0.00}"));
        }
    }

    private async Task RefreshBeliefsAsync()
    {
        using var beliefs = await this.runtime
            .GetAsync("/beliefs", this.lifetime.Token).ConfigureAwait(true);
        if (beliefs is null)
        {
            return;
        }

        this.state.Beliefs.Clear();
        foreach (var item in beliefs.RootElement.EnumerateArray())
        {
            var supporting = item.GetProperty("supportingObservations").GetArrayLength();
            this.state.Beliefs.Add(new SidebarItem(
                item.GetProperty("conclusionId").GetGuid().ToString("N")[..8],
                item.GetProperty("statement").GetString() ?? string.Empty,
                $"{item.GetProperty("confidence").GetDouble():0.00} · "
                + $"{item.GetProperty("source").GetString()} · {supporting} obs"));
        }
    }
}
