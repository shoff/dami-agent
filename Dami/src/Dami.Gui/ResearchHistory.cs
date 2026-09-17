using Dami.Contracts.Research;

namespace Dami.Gui;

/// <summary>Research outcomes available in the library filter.</summary>
public enum ResearchHistoryFilter
{
    /// <summary>Every retained outcome.</summary>
    All,
    /// <summary>Source collection is active.</summary>
    Running,
    /// <summary>Source collection finished.</summary>
    Completed,
    /// <summary>Failed, cancelled or interrupted runs.</summary>
    Stopped,
}

/// <summary>Filtering and stable selection for live research history.</summary>
public sealed class ResearchHistory
{
    private IReadOnlyList<ResearchRunSummary> runs = [];
    private string[] words = [];
    private ResearchHistoryFilter filter;
    private Guid? selectedId;

    /// <summary>The entries matching the active query and status.</summary>
    public IReadOnlyList<ResearchRunSummary> Visible { get; private set; } = [];

    /// <summary>The selected visible entry, including its newest revision.</summary>
    public ResearchRunSummary? Selected => this.Visible.FirstOrDefault(run => run.RunId == this.selectedId);

    /// <summary>Replaces the server history without losing a still-visible selection.</summary>
    public void Update(IReadOnlyList<ResearchRunSummary> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        this.runs = history;
        this.Refresh();
    }

    /// <summary>Filters question, source URL and trace, together with the observed outcome.</summary>
    public void Filter(string query, ResearchHistoryFilter status)
    {
        ArgumentNullException.ThrowIfNull(query);
        this.words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        this.filter = status;
        this.Refresh();
    }

    /// <summary>Selects a visible run.</summary>
    public void Select(Guid id) => this.selectedId = this.Visible.Any(run => run.RunId == id) ? id : null;

    private void Refresh()
    {
        this.Visible = this.runs.Where(this.Matches).ToArray();
        if (this.Selected is null)
        {
            this.selectedId = this.Visible.FirstOrDefault()?.RunId;
        }
    }

    private bool Matches(ResearchRunSummary run)
    {
        var state = this.filter switch
        {
            ResearchHistoryFilter.Running => run.Status == ResearchRunStatus.Running,
            ResearchHistoryFilter.Completed => run.Status == ResearchRunStatus.Completed,
            ResearchHistoryFilter.Stopped => run.Status is ResearchRunStatus.Failed or ResearchRunStatus.Cancelled or ResearchRunStatus.Interrupted,
            _ => true,
        };
        var searchable = $"{run.Question} {run.Seed} {run.TraceId:D}";
        return state && this.words.All(word => searchable.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}
