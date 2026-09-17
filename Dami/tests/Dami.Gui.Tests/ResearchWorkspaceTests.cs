using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Dami.Contracts.Research;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class ResearchWorkspaceTests
{
    [Fact]
    public async Task The_Editor_Should_Have_The_Workspace_Until_It_Is_Closed()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var view = new ResearchWorkspace();
            view.FindControl<Button>("NewResearch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var resultsWhileEditing = view.FindControl<Grid>("ResearchResults")!.IsVisible;
            view.FindControl<Button>("CloseResearchForm")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal((false, true), (resultsWhileEditing, view.FindControl<Grid>("ResearchResults")!.IsVisible));
        });
    }

    [Fact]
    public async Task An_Empty_Completed_Run_Should_Explain_Recovery()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var run = new ResearchRun(Guid.NewGuid(), Guid.NewGuid(), "Evidence", new Uri("https://public.example/"),
                DateTimeOffset.Parse("2026-09-11T12:00:00Z"))
            { Status = ResearchRunStatus.Completed };
            var view = new ResearchWorkspace();
            view.PresentHistory([run.Summarize()]);
            view.ShowRun(run);

            Assert.Equal((true, "No sources collected"),
                (view.FindControl<SelectableTextBlock>("RunNotice")!.Text!.Contains("Edit and rerun", StringComparison.Ordinal),
                    new ResearchRunRow(run.Summarize()).State));
        });
    }

    [Fact]
    public async Task Saved_Run_Should_Surface_Skipped_Reads_In_Its_Progress()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var run = new ResearchRun(Guid.NewGuid(), Guid.NewGuid(), "Evidence", new Uri("https://public.example/"),
                DateTimeOffset.Parse("2026-09-11T12:00:00Z"))
            { Issues = [new ResearchIssue(new Uri("https://public.example/missing"), "Source offline")] };
            var view = new ResearchWorkspace();
            view.PresentHistory([run.Summarize()]);
            view.ShowRun(run);

            Assert.Contains("1 skipped", view.FindControl<TextBlock>("RunProgress")!.Text ?? "", StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task New_Research_Should_Open_A_Fresh_Form_After_Editing_A_Saved_Run()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var run = new ResearchRun(Guid.NewGuid(), Guid.NewGuid(), "Earlier topic", new Uri("https://public.example/"),
                DateTimeOffset.Parse("2026-09-11T12:00:00Z"));
            var view = new ResearchWorkspace();
            view.PresentHistory([run.Summarize()]);
            view.ShowRun(run);
            view.FindControl<Button>("EditResearch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            view.FindControl<Button>("NewResearch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal((true, "", "", "New research", "Start research"),
                (view.FindControl<Border>("ResearchForm")!.IsVisible, view.FindControl<TextBox>("ResearchQuestion")!.Text,
                    view.FindControl<TextBox>("ResearchSeed")!.Text, view.FindControl<TextBlock>("ResearchFormTitle")!.Text,
                    view.FindControl<Button>("StartResearch")!.Content));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Edit_And_Rerun_Should_Preserve_The_Old_Report_And_Submit_The_Edited_Question(bool automatic)
    {
        var run = new ResearchRun(Guid.NewGuid(), Guid.NewGuid(), "Original topic", new Uri("https://public.example/"),
            DateTimeOffset.Parse("2026-09-11T12:00:00Z"))
        { AutomaticSources = automatic, Answer = "Earlier report" };
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var submitted = (Seed: "", Question: "");
            var view = new ResearchWorkspace();
            view.Initialize(new RuntimeClient(), (seed, question) =>
            { submitted = (seed, question); return new ResearchLaunchResult("Accepted", "Started"); }, CancellationToken.None);
            view.PresentHistory([run.Summarize()]);
            view.ShowRun(run);
            view.FindControl<Button>("EditResearch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            view.FindControl<TextBox>("ResearchQuestion")!.Text += " with a narrower scope";
            view.ShowRun(run);
            view.FindControl<Button>("StartResearch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal((automatic ? "" : run.Seed.AbsoluteUri, "Original topic with a narrower scope", "Earlier report", false),
                (submitted.Seed, submitted.Question, view.FindControl<ReplyView>("ResearchAnswer")!.Text,
                    view.FindControl<Border>("ResearchForm")!.IsVisible));
        });
    }

    [Fact]
    public async Task Report_Code_Copy_Should_Be_Handled_Inside_The_Research_Workspace()
    {
        var run = new ResearchRun(Guid.NewGuid(), Guid.NewGuid(), "Example", new Uri("https://public.example/"),
            DateTimeOffset.Parse("2026-09-11T12:00:00Z"))
        { Answer = "```bash\ncurl https://public.example/\n```" };
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var view = new ResearchWorkspace();
            view.PresentHistory([run.Summarize()]);
            view.ShowRun(run);
            var copy = view.FindControl<ReplyView>("ResearchAnswer")!.GetLogicalDescendants().OfType<Button>().Single();
            var click = new RoutedEventArgs(Button.ClickEvent, copy);
            view.OnContentClick(copy, click);
            Assert.True(click.Handled, $"Source={click.Source?.GetType().Name}; tag={copy.Tag}; payload={copy.CommandParameter?.GetType().Name}");
            Assert.Equal("curl https://public.example/\n", copy.CommandParameter);
        });
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Start_Form_Should_Close_Only_After_A_Request_Is_Accepted(bool accepted, bool visible)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var view = new ResearchWorkspace();
            view.Initialize(new RuntimeClient(), (_, _) => new ResearchLaunchResult(accepted ? "Question" : null, "Status"),
                CancellationToken.None);
            view.FindControl<Border>("ResearchForm")!.IsVisible = true;
            view.FindControl<Button>("StartResearch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(visible, view.FindControl<Border>("ResearchForm")!.IsVisible);
        });
    }

    [Fact]
    public async Task Late_Detail_Response_Should_Not_Replace_A_New_Selection()
    {
        var first = new ResearchRun(Guid.NewGuid(), Guid.NewGuid(), "First", new Uri("https://public.example/"),
            DateTimeOffset.Parse("2026-09-11T12:00:00Z"));
        var next = first with { RunId = Guid.NewGuid(), Question = "Next" };
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var view = new ResearchWorkspace();
            view.PresentHistory([first.Summarize(), next.Summarize()]);
            view.ShowRun(first);
            view.FindControl<ListBox>("RunList")!.SelectedIndex = 1;
            view.ShowRun(next);
            view.ShowRun(first);
            Assert.Equal("Next", view.FindControl<TextBlock>("RunTitle")!.Text);
            Assert.Equal(next.RunId, view.SelectedRunId);
        });
    }

    [Fact]
    public async Task Saved_Run_Should_Show_Its_Question_And_Inert_Source_Text()
    {
        var seed = new Uri("https://public.example/start");
        var run = new ResearchRun(Guid.NewGuid(), Guid.NewGuid(), "Where is the evidence?", seed,
            DateTimeOffset.Parse("2026-09-11T12:00:00Z"))
        {
            Status = ResearchRunStatus.Completed,
            Findings = [new ResearchFinding(new ResearchPage(seed, 200, "Evidence", "<script>untrusted text</script>"), 0, [seed])],
        };
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var view = new ResearchWorkspace();
            view.PresentHistory([run.Summarize()]);
            view.ShowRun(run);
            Assert.Equal(run.Question, view.FindControl<TextBlock>("RunTitle")!.Text);
            Assert.Equal("<script>untrusted text</script>", view.FindControl<SelectableTextBlock>("SourceText")!.Text);
            Assert.True(view.FindControl<Button>("CopyReport")!.IsEnabled);
            Assert.Equal(run.RunId, view.SelectedRunId);
        });
    }
}
