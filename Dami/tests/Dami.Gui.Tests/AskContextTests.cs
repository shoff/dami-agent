using Xunit;

namespace Dami.Gui.Tests;

public sealed class AskContextTests
{
    private static readonly DateTimeOffset at = new(2026, 8, 28, 23, 21, 0, TimeSpan.Zero);

    [Fact]
    public void Describe_Should_Carry_What_A_Pass_Actually_Did()
    {
        // A description that omits the alert count produces a confident answer that misses
        // the point, which is the failure mode this whole feature has to avoid.
        var run = new WorkerRun(at, "Completed", "386cb0e7", Guid.NewGuid(), 3, 4, 1, 5.4, 20);

        var described = AskContext.Describe(run, "ignored");

        Assert.Contains("1 alert", described, StringComparison.Ordinal);
        Assert.Contains("386cb0e7", described, StringComparison.Ordinal);
        Assert.Contains("5.4s", described, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_Should_Say_When_A_Service_Was_Refused_While_Reporting_Success()
    {
        // The distinction the workers view exists for has to survive into the prompt, or
        // the model will read "Completed" and reassure him.
        var service = new WorkerRow(
            "interest-scout", "Completed", "23 h ago", 6, [], "Nightly", "due in 1 h", false, 15, 24, 6);

        var described = AskContext.Describe(service, "ignored");

        Assert.Contains("interest-scout", described, StringComparison.Ordinal);
        Assert.Contains("6 alerts", described, StringComparison.Ordinal);
        Assert.Contains("still reporting as completed", described, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_Should_Not_Claim_Trouble_For_A_Healthy_Service()
    {
        var service = new WorkerRow(
            "curator", "Completed", "2 h ago", 5, [], "Nightly", "due in 22 h", false, 0, 0, 0);

        Assert.DoesNotContain("refused", AskContext.Describe(service, ""), StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_Should_Carry_A_Surfacing_Link_So_The_Model_Can_See_What_It_Is()
    {
        var item = new SidebarItem(
            "c52239ad", "Anger, Anxiety and Agency", "interest-scout · confidence 0.61",
            SidebarKind.Surfacing, "https://lucumr.pocoo.org/2026/8/24/anger-anxiety-agency/");

        var described = AskContext.Describe(item, "ignored");

        Assert.Contains("lucumr.pocoo.org", described, StringComparison.Ordinal);
        Assert.Contains("Surfacing", described, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_Should_Carry_A_Flagged_Event_With_Its_Label()
    {
        var moment = new PassEvent(
            "22:47:48", "+4.3s", "EgressCompleted", "hnrss.org answered 429", "Succeeded", 10, 20, true);

        var described = AskContext.Describe(moment, "ignored");

        Assert.Contains("answered 429", described, StringComparison.Ordinal);
        Assert.Contains("wanting a look", described, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_Should_Fall_Back_To_What_Is_Visible()
    {
        // Right-clicking a heading or a label still has to ask something sensible.
        Assert.Equal("PROACTIVE SERVICES", AskContext.Describe(null, "  PROACTIVE SERVICES  "));
    }

    [Fact]
    public void Prompt_Should_Tell_The_Model_To_Admit_What_It_Cannot_See()
    {
        var prompt = AskContext.Prompt("a pass that produced nothing", "why did this fail?");

        Assert.Contains("why did this fail?", prompt, StringComparison.Ordinal);
        Assert.Contains("a pass that produced nothing", prompt, StringComparison.Ordinal);
        Assert.Contains("rather than guessing", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_Should_Say_So_When_Nothing_Identifiable_Was_Clicked()
    {
        var prompt = AskContext.Prompt(string.Empty, "what is this?");

        Assert.Contains("nothing identifiable", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_Should_Refuse_An_Empty_Question()
    {
        Assert.Throws<ArgumentException>(() => AskContext.Prompt("context", "   "));
    }
}
