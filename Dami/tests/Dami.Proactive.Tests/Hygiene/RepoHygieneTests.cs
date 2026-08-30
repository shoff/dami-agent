using Dami.Contracts.Proactive;
using Dami.Proactive.Hygiene;
using Xunit;

namespace Dami.Proactive.Tests.Hygiene;

public sealed class RepoHygieneTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 29, 20, 0, 0, TimeSpan.Zero);
    private static readonly RepoHygieneOptions options = new();

    private static RepoState Clean(
        int ahead = 0,
        DateTimeOffset? oldest = null,
        string[]? dirty = null,
        DateTimeOffset? fetched = null,
        string[]? trackedDdl = null,
        bool upstream = true)
    {
        return new RepoState(
            true, "main", upstream, ahead, 0, oldest, dirty ?? [],
            fetched ?? now.AddHours(-1), trackedDdl ?? []);
    }

    [Fact]
    public void Assess_Should_Say_Nothing_About_An_Ordinary_Working_Copy()
    {
        // The bar for a nightly watcher: silent on a good day, or it gets muted and stops
        // being a watcher at all.
        Assert.Empty(RepoHygiene.Assess(Clean(), [], now, options));
    }

    [Fact]
    public void Assess_Should_Tolerate_A_Session_In_Progress()
    {
        // Two commits from this afternoon are normal, not a finding.
        var state = Clean(ahead: 2, oldest: now.AddHours(-3));

        Assert.Empty(RepoHygiene.Assess(state, [], now, options));
    }

    [Fact]
    public void Assess_Should_Notice_Work_Piling_Up()
    {
        var state = Clean(ahead: 52, oldest: now.AddDays(-4));

        var finding = Assert.Single(RepoHygiene.Assess(state, [], now, options));
        Assert.Contains("52 commit(s)", finding, StringComparison.Ordinal);
        Assert.Contains("4 day(s)", finding, StringComparison.Ordinal);
    }

    [Fact]
    public void Assess_Should_Notice_A_Single_Commit_That_Has_Been_Waiting()
    {
        // Count alone would miss this: one commit stranded for a week matters, and the
        // whole point is catching work that is quietly not where it should be.
        var state = Clean(ahead: 1, oldest: now.AddDays(-7));

        Assert.Single(RepoHygiene.Assess(state, [], now, options));
    }

    [Fact]
    public void Assess_Should_Lead_With_A_Migration_The_Repository_Does_Not_Have()
    {
        // The real case, 2026-08-29: 035 was applied to dami-data and never committed.
        // It comes first because it means two systems disagree about reality, not merely
        // that work is late.
        var state = Clean(ahead: 52, oldest: now.AddDays(-4), trackedDdl: ["034_task_work_activity.sql"]);

        var findings = RepoHygiene.Assess(
            state, ["034_task_work_activity.sql", "035_proactive_run_cadence.sql"], now, options);

        Assert.Contains("035_proactive_run_cadence.sql", findings[0], StringComparison.Ordinal);
        Assert.Contains("not tracked in git", findings[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Assess_Should_Not_Complain_When_Every_Applied_Migration_Is_Tracked()
    {
        var state = Clean(trackedDdl: ["034.sql", "035.sql"]);

        Assert.Empty(RepoHygiene.Assess(state, ["034.sql", "035.sql"], now, options));
    }

    [Fact]
    public void Assess_Should_Say_When_The_Remote_Has_Not_Been_Looked_At()
    {
        // A stale fetch makes the ahead count stale too, which is worse than not knowing
        // because it still looks like an answer.
        var state = Clean(fetched: now.AddDays(-4));

        var finding = Assert.Single(RepoHygiene.Assess(state, [], now, options));
        Assert.Contains("not been fetched", finding, StringComparison.Ordinal);
    }

    [Fact]
    public void Assess_Should_Treat_A_Branch_With_No_Upstream_As_Unpushed_Entirely()
    {
        var state = Clean(ahead: 3, upstream: false);

        var finding = Assert.Single(RepoHygiene.Assess(state, [], now, options));
        Assert.Contains("no upstream", finding, StringComparison.Ordinal);
    }

    [Fact]
    public void Assess_Should_Summarise_A_Dirty_Tree_Without_Listing_All_Of_It()
    {
        var state = Clean(dirty: ["a.cs", "b.cs", "c.cs", "d.cs", "e.cs", "f.cs", "g.cs"]);

        var finding = Assert.Single(RepoHygiene.Assess(state, [], now, options));
        Assert.Contains("7 uncommitted path(s)", finding, StringComparison.Ordinal);
        Assert.Contains("and 2 more", finding, StringComparison.Ordinal);
        Assert.DoesNotContain("g.cs", finding, StringComparison.Ordinal);
    }

    [Fact]
    public void Assess_Should_Report_Everything_Adrift_At_Once()
    {
        var state = Clean(
            ahead: 52, oldest: now.AddDays(-4), dirty: ["x.cs"],
            fetched: now.AddDays(-4), trackedDdl: []);

        var findings = RepoHygiene.Assess(state, ["035.sql"], now, options);

        Assert.Equal(4, findings.Count);
    }
}
