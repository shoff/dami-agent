using Dami.Contracts.Research;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class ResearchHistoryTests
{
    [Fact]
    public void Refresh_Should_Preserve_Selection_And_Apply_Query_And_State_Together()
    {
        var older = new ResearchRun(Guid.NewGuid(), Guid.NewGuid(), "County permit data", new Uri("https://civic.example/"),
            DateTimeOffset.Parse("2026-09-11T12:00:00Z")) { Status = ResearchRunStatus.Completed };
        var newer = older with { RunId = Guid.NewGuid(), Question = "Weather observations", StartedAt = older.StartedAt.AddHours(1),
            Status = ResearchRunStatus.Running };
        var history = new ResearchHistory();
        history.Update([newer.Summarize(), older.Summarize()]);
        history.Select(older.RunId);
        history.Update([(newer with { Revision = 2 }).Summarize(), (older with { Revision = 3 }).Summarize()]);
        Assert.Equal((older.RunId, 3), (history.Selected!.RunId, history.Selected.Revision));
        history.Filter(" CIVIC  permit ", ResearchHistoryFilter.Completed);
        Assert.Equal(older.RunId, Assert.Single(history.Visible).RunId);
        history.Filter("permit", ResearchHistoryFilter.Running);
        Assert.Empty(history.Visible);
        Assert.Null(history.Selected);
        history.Filter("", ResearchHistoryFilter.All);
        Assert.Equal(newer.RunId, history.Selected!.RunId);
    }
}
