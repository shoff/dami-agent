using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Media;
using Dami.Contracts.Scheduling;

namespace Dami.Gui;

/// <summary>Conversational schedule creation beside durable job status.</summary>
public sealed class JobsWindow : Window
{
    private static readonly JsonSerializerOptions json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly RuntimeClient runtime;
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<string> conversation = [];
    private readonly StackPanel transcript = new() { Spacing = 6 };
    private readonly StackPanel jobs = new() { Spacing = 8 };
    private readonly TextBox input = new() { PlaceholderText = "Describe what you want scheduled…" };
    private readonly Button send = new() { Content = "send" };
    private readonly Button confirm = new() { Content = "confirm and activate", IsVisible = false };
    private ScheduledJobProposal? proposal;

    /// <summary>Creates the jobs dashboard using the authenticated runtime client.</summary>
    public JobsWindow(RuntimeClient runtime)
    {
        this.runtime = runtime;
        this.Title = "Dami — Scheduled Jobs";
        this.Width = 1100;
        this.Height = 720;
        this.Background = Brush.Parse("#101418");
        this.Content = this.BuildLayout();
        this.send.Click += (_, _) => _ = this.SendAsync();
        this.confirm.Click += (_, _) => _ = this.ConfirmAsync();
        this.Closed += (_, _) => this.lifetime.Cancel();
        this.Opened += (_, _) => _ = this.RefreshAsync();
    }

    private Control BuildLayout()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,3*"), Margin = new Avalonia.Thickness(16) };
        grid.Children.Add(this.Panel("CREATE WITH DAMI", this.ConversationBody(), 0));
        grid.Children.Add(this.Panel("JOBS", new ScrollViewer { Content = this.jobs }, 1));
        return grid;
    }

    private Control ConversationBody()
    {
        var controls = new StackPanel { Spacing = 10 };
        controls.Children.Add(new ScrollViewer { Content = this.transcript, MaxHeight = 500 });
        controls.Children.Add(this.input);
        controls.Children.Add(this.send);
        controls.Children.Add(this.confirm);
        return controls;
    }

    private Border Panel(string heading, Control body, int column)
    {
        var content = new DockPanel();
        content.Children.Add(new TextBlock
        {
            Text = heading,
            FontSize = 11,
            Foreground = Brush.Parse("#7A8694"),
            Margin = new Avalonia.Thickness(0, 0, 0, 10),
            [DockPanel.DockProperty] = Dock.Top,
        });
        content.Children.Add(body);
        var panel = new Border
        {
            Background = Brush.Parse("#171D24"),
            CornerRadius = new Avalonia.CornerRadius(8),
            Padding = new Avalonia.Thickness(14),
            Margin = column == 0 ? new Avalonia.Thickness(0, 0, 6, 0) : new Avalonia.Thickness(6, 0, 0, 0),
            Child = content,
        };
        Grid.SetColumn(panel, column);
        return panel;
    }

    private async Task SendAsync()
    {
        var text = this.input.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        this.AddLine("you", text);
        this.conversation.Add(text);
        this.input.Text = string.Empty;
        this.send.IsEnabled = false;
        using var document = await this.runtime.PostAsync(
            "/jobs/plan", new { messages = this.conversation }, this.lifetime.Token);
        this.send.IsEnabled = true;
        if (document is null)
        {
            this.AddLine("dami", "The runtime did not answer.");
            return;
        }

        var reply = document.RootElement.Deserialize<ScheduledJobPlanningReply>(json);
        this.ShowReply(reply);
    }

    private void ShowReply(ScheduledJobPlanningReply? reply)
    {
        if (reply?.Question is { } question)
        {
            this.conversation.Add(question);
            this.AddLine("dami", question);
            return;
        }

        this.proposal = reply?.Proposal;
        if (this.proposal is null)
        {
            this.AddLine("dami", "The scheduling plan was not valid.");
            return;
        }

        var arguments = this.proposal.Arguments.Count == 0
            ? string.Empty
            : " " + string.Join(' ', this.proposal.Arguments.Select(Quote));
        this.AddLine("dami", $"Ready to create:\n{this.proposal.Name}\n{this.proposal.Description}\n{this.proposal.Kind}: {this.proposal.Payload}{arguments}\n{this.proposal.CronExpression} · {this.proposal.TimeZoneId}");
        this.confirm.IsVisible = true;
    }

    private async Task ConfirmAsync()
    {
        if (this.proposal is null)
        {
            return;
        }

        this.confirm.IsEnabled = false;
        using var draftDocument = await this.runtime.PostAsync(
            "/jobs/drafts", this.proposal, this.lifetime.Token);
        var draft = draftDocument?.RootElement.Deserialize<ScheduledJob>(json);
        if (draft is null)
        {
            this.AddLine("dami", "The draft could not be saved.");
            this.confirm.IsEnabled = true;
            return;
        }

        using var activeDocument = await this.runtime.PostAsync(
            $"/jobs/{draft.JobId}/confirm", new { }, this.lifetime.Token);
        var active = activeDocument?.RootElement.Deserialize<ScheduledJob>(json);
        this.AddLine("dami", active is null ? "The job was saved but could not be activated." : "Scheduled. The job is active.");
        this.ResetInterview();
        await this.RefreshAsync().ConfigureAwait(true);
    }

    private async Task RefreshAsync()
    {
        using var document = await this.runtime.GetAsync("/jobs", this.lifetime.Token);
        var rows = document?.RootElement.Deserialize<ScheduledJob[]>(json) ?? [];
        this.jobs.Children.Clear();
        foreach (var job in rows)
        {
            this.jobs.Children.Add(JobCard(job));
        }

        if (rows.Length == 0)
        {
            this.jobs.Children.Add(this.Muted("No jobs yet."));
        }
    }

    private void ResetInterview()
    {
        this.proposal = null;
        this.conversation.Clear();
        this.confirm.IsVisible = false;
        this.confirm.IsEnabled = true;
    }

    private void AddLine(string who, string text)
    {
        this.transcript.Children.Add(new TextBlock
        {
            Text = $"{who}: {text}",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush.Parse(who == "you" ? "#D7DDE4" : "#5AA9E6"),
        });
    }

    private static Border JobCard(ScheduledJob job)
    {
        var next = job.NextRunAt?.ToLocalTime().ToString("g") ?? "not scheduled";
        var last = job.LastRunAt is null ? "never" : $"{job.LastRunAt.Value.ToLocalTime():g} · {job.LastRunStatus}";
        return new Border
        {
            Background = Brush.Parse("#0D1116"),
            Padding = new Avalonia.Thickness(10),
            CornerRadius = new Avalonia.CornerRadius(5),
            Child = new TextBlock
            {
                Text = $"{job.Name}  [{job.Status}]\n{job.Description}\n{job.Kind} · {job.CronExpression} · {job.TimeZoneId}\nnext: {next}   last: {last}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush.Parse("#D7DDE4"),
            },
        };
    }

    private TextBlock Muted(string text) => new() { Text = text, Foreground = Brush.Parse("#7A8694") };

    private static string Quote(string value) => value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;

    private sealed record ScheduledJobPlanningReply(string? Question, ScheduledJobProposal? Proposal);
}
