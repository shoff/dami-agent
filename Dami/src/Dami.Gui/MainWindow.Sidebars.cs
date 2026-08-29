
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Dami.Gui;

/// <summary>What Dami wants Steve to notice, and what it currently believes.</summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Acts on an attention row in place. A verdict trains the taste model; an approval
    /// resolves and the runtime executes what it authorised. Either way the list is
    /// re-read afterwards, so a row that is no longer waiting stops being shown.
    /// </summary>
    private void OnAttentionAction(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button button || button.Tag is not string action
            || button.DataContext is not SidebarItem item)
        {
            return;
        }

        e.Handled = true;
        if (action == "open")
        {
            this.OpenLink(item);
            return;
        }

        _ = this.ApplyAttentionAsync(item, action);
    }

    /// <summary>
    /// Hands the link to the desktop. Deliberately the only outbound thing this panel
    /// does: reading is the step that has to happen before a verdict means anything.
    /// </summary>
    private void OpenLink(SidebarItem item)
    {
        if (item.Link is not { } link)
        {
            return;
        }

        try
        {
            using var opened = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(link) { UseShellExecute = true });
            this.statusLine.Text = $"opened {item.Source}";
        }
        catch (Exception exception)
        {
            this.statusLine.Text = $"could not open {item.Source}: {exception.Message}";
        }
    }

    private async Task ApplyAttentionAsync(SidebarItem item, string action)
    {
        try
        {
            var (path, body) = action switch
            {
                "approve" => ($"/approvals/{item.Id}/resolve", (object)new { approve = true, note = (string?)null }),
                "deny" => ($"/approvals/{item.Id}/resolve", new { approve = false, note = (string?)null }),
                _ => ($"/surfacings/{item.Id}/feedback", new { verdict = action, note = (string?)null }),
            };
            using var reply = await this.runtime
                .PostAsync(path, body, this.lifetime.Token).ConfigureAwait(true);
            this.statusLine.Text = reply is null
                ? $"{action} failed on {item.Id}"
                : $"{action} recorded on {item.Id}";
            await this.RefreshSidebarsAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            this.statusLine.Text = $"{action} failed: {exception.Message}";
        }
    }

    private async Task RefreshSidebarsAsync()
    {
        var attention = new List<SidebarItem>();
        await this.AddApprovalsAsync(attention).ConfigureAwait(true);
        await this.AddSurfacingsAsync(attention).ConfigureAwait(true);
        await this.AddTodayAsync(attention).ConfigureAwait(true);
        Reconcile.Sync(this.state.Attention, attention);

        await this.RefreshBeliefsAsync().ConfigureAwait(true);
    }

    /// <summary>The board's questions for Steve, the civic week, and network problems (K4).</summary>
    private async Task AddTodayAsync(List<SidebarItem> into)
    {
        using var boards = await this.runtime.GetAsync("/task-boards", this.lifetime.Token).ConfigureAwait(true);
        foreach (var board in boards?.RootElement.EnumerateArray() ?? default)
        {
            using var snapshot = await this.runtime
                .GetAsync($"/task-boards/{board.GetProperty("boardId").GetGuid():D}", this.lifetime.Token)
                .ConfigureAwait(true);
            if (snapshot is not null)
            {
                into.AddRange(TodayDigest.BoardQuestions(snapshot.RootElement.GetProperty("tasks")));
            }
        }

        using var civic = await this.runtime.GetAsync("/domains/civic", this.lifetime.Token).ConfigureAwait(true);
        if (civic is not null)
        {
            into.AddRange(TodayDigest.CivicWeek(civic.RootElement, DateOnly.FromDateTime(DateTime.Today)));
        }

        using var network = await this.runtime.GetAsync("/domains/network", this.lifetime.Token).ConfigureAwait(true);
        if (network is not null)
        {
            into.AddRange(TodayDigest.NetworkProblems(network.RootElement));
        }
    }

    private async Task AddApprovalsAsync(List<SidebarItem> into)
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
            into.Add(new SidebarItem(
                id,
                "APPROVAL · " + (item.GetProperty("action").GetString() ?? string.Empty),
                $"requested by {item.GetProperty("requestedBy").GetString()} · "
                + $"scope {item.GetProperty("scope").GetString()}",
                SidebarKind.Approval));
        }
    }

    private async Task AddSurfacingsAsync(List<SidebarItem> into)
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
            into.Add(new SidebarItem(
                id,
                item.GetProperty("title").GetString() ?? string.Empty,
                $"{item.GetProperty("serviceName").GetString()} · "
                + $"confidence {item.GetProperty("confidence").GetDouble():0.00}",
                SidebarKind.Surfacing,
                item.GetProperty("body").GetString() ?? string.Empty));
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

        var current = new List<SidebarItem>();
        foreach (var item in beliefs.RootElement.EnumerateArray())
        {
            var supporting = item.GetProperty("supportingObservations").GetArrayLength();
            current.Add(new SidebarItem(
                item.GetProperty("conclusionId").GetGuid().ToString("N")[..8],
                item.GetProperty("statement").GetString() ?? string.Empty,
                $"confidence {item.GetProperty("confidence").GetDouble():0.00} · "
                + $"from {item.GetProperty("source").GetString()} · "
                + $"{supporting} supporting observation{(supporting == 1 ? string.Empty : "s")}"));
        }

        Reconcile.Sync(this.state.Beliefs, current);
    }
}
