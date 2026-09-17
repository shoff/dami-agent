namespace Dami.Gui;

/// <summary>Research uses the existing authenticated runtime and cancellable conversation flow.</summary>
public sealed partial class MainWindow
{
    private ResearchWorkspace researchWorkspace = null!;

    private void InitializeResearch()
    {
        this.researchWorkspace = Require<ResearchWorkspace>(this, "ResearchPage");
        this.researchWorkspace.Initialize(this.runtime, this.StartResearch, this.lifetime.Token);
        this.workspaceTabs.SelectionChanged += (_, _) =>
        {
            if (this.workspaceTabs.SelectedIndex == 7)
            {
                _ = this.researchWorkspace.RefreshAsync();
            }
        };
    }

    private ResearchLaunchResult StartResearch(string seed, string question)
    {
        var launch = ResearchLaunch.Prepare(seed, question, this.CaptureDraft(), this.chatTurns.IsRunning);
        if (launch.Prompt is not null)
        {
            this.input.Text = launch.Prompt;
            _ = this.SendAsync();
        }

        return launch;
    }
}
