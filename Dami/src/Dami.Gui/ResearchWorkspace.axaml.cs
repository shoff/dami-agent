using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Dami.Contracts.Research;

namespace Dami.Gui;

/// <summary>The native research library and inert source reader.</summary>
public sealed partial class ResearchWorkspace : UserControl
{
    private readonly ResearchHistory history = new();
    private ResearchRun? selectedRun;
    private bool rendering;

    /// <summary>Creates the library's native controls.</summary>
    public ResearchWorkspace()
    {
        AvaloniaXamlLoader.Load(this);
        this.Control<TextBox>("ResearchSearch").TextChanged += (_, _) => this.FilterHistory();
        this.Control<ComboBox>("ResearchFilter").SelectionChanged += (_, _) => this.FilterHistory();
        this.Control<ListBox>("RunList").SelectionChanged += (_, _) => this.SelectRun();
        this.Control<ComboBox>("SourceChoice").SelectionChanged += (_, _) => this.ShowSource();
        this.Control<Button>("NewResearch").Click += (_, _) => this.OpenForm();
        this.Control<Button>("EditResearch").Click += (_, _) => this.EditRun();
        this.Control<Button>("CloseResearchForm").Click += (_, _) => this.Control<Border>("ResearchForm").IsVisible = false;
        this.Control<Button>("CopyReport").Click += (_, _) => _ = this.CopyAsync(
            this.selectedRun is null ? null : ResearchPresentation.Export(this.selectedRun), "Report copied.");
        this.Control<Button>("CopySource").Click += (_, _) => _ = this.CopyAsync(
            (this.Control<ComboBox>("SourceChoice").SelectedItem as ResearchSourceRow)?.Finding.Page.Text, "Source text copied.");
        this.Control<Border>("ResearchForm").PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty)
            {
                this.Control<Grid>("ResearchResults").IsVisible = !this.Control<Border>("ResearchForm").IsVisible;
            }
        };
        this.AddHandler(Button.ClickEvent, this.OnContentClick);
    }

    /// <summary>The selected research identity, stable across live refresh.</summary>
    public Guid? SelectedRunId => this.history.Selected?.RunId;

    /// <summary>Applies server history without resetting a still-visible selection.</summary>
    public void PresentHistory(IReadOnlyList<ResearchRunSummary> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        this.history.Update(runs);
        this.RenderHistory();
        this.Control<TextBlock>("HistoryCount").Text = $"{runs.Count} loaded";
        this.Control<TextBlock>("HistoryEmpty").Text = runs.Count == 0
            ? "No saved runs yet. Start a research question to build your library."
            : "No runs match these filters.";
    }

    private T Control<T>(string name) where T : Control => this.FindControl<T>(name)
        ?? throw new InvalidOperationException($"Research control '{name}' is missing.");

    private void OpenForm()
    {
        this.Control<TextBox>("ResearchQuestion").Text = "";
        this.Control<TextBox>("ResearchSeed").Text = "";
        this.Control<TextBlock>("ResearchFormTitle").Text = "New research";
        this.Control<Button>("StartResearch").Content = "Start research";
        this.Control<TextBlock>("LaunchMessage").Text =
            "Leave the starting URL empty for Dami to choose relevant sites. Each run saves a separate report; Esc stops the active reply.";
        this.Control<Border>("ResearchForm").IsVisible = true;
        this.Control<TextBox>("ResearchQuestion").Focus();
    }

    private void EditRun()
    {
        if (this.selectedRun is not { } run) { return; }
        this.Control<TextBox>("ResearchQuestion").Text = run.Question;
        this.Control<TextBox>("ResearchSeed").Text = run.AutomaticSources ? "" : run.Seed.AbsoluteUri;
        this.Control<TextBlock>("ResearchFormTitle").Text = "Edit and rerun";
        this.Control<Button>("StartResearch").Content = "Run again";
        this.Control<TextBlock>("LaunchMessage").Text =
            $"Edit the question or starting URL. A new dated run will be saved; the report from {run.StartedAt.ToLocalTime():MMM d, h:mm tt} stays available.";
        this.Control<Border>("ResearchForm").IsVisible = true;
        this.Control<TextBox>("ResearchQuestion").Focus();
    }

    private void FilterHistory()
    {
        this.history.Filter(this.Control<TextBox>("ResearchSearch").Text ?? "",
            (ResearchHistoryFilter)Math.Max(0, this.Control<ComboBox>("ResearchFilter").SelectedIndex));
        this.RenderHistory();
        this.RequestSelected();
    }

    private void RenderHistory()
    {
        this.rendering = true;
        var list = this.Control<ListBox>("RunList");
        var rows = this.history.Visible.Select(run => new ResearchRunRow(run)).ToArray();
        if (!(list.ItemsSource?.Cast<ResearchRunRow>() ?? []).SequenceEqual(rows))
        {
            list.ItemsSource = rows;
        }

        list.SelectedItem = list.ItemsSource?.Cast<ResearchRunRow>().FirstOrDefault(row => row.Run.RunId == this.SelectedRunId);
        this.Control<TextBlock>("HistoryEmpty").IsVisible = rows.Length == 0;
        this.Control<StackPanel>("ResearchWelcome").IsVisible = this.SelectedRunId is null;
        this.Control<Grid>("RunContent").IsVisible = this.SelectedRunId is not null && this.selectedRun?.RunId == this.SelectedRunId;
        this.rendering = false;
    }

    private void SelectRun()
    {
        if (!this.rendering && this.Control<ListBox>("RunList").SelectedItem is ResearchRunRow row)
        {
            this.history.Select(row.Run.RunId);
            this.RequestSelected();
        }
    }

    private void RequestSelected() => this.SelectionRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Requests the selected artifact when selection or filters change.</summary>
    public event EventHandler? SelectionRequested;

    /// <summary>Displays a retained artifact; original source content remains plain selectable text.</summary>
    public void ShowRun(ResearchRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (this.history.Selected is not { } summary || summary.RunId != run.RunId || summary.Revision > run.Revision)
        {
            return;
        }

        if (this.selectedRun?.RunId == run.RunId && this.selectedRun.Revision >= run.Revision)
        {
            return;
        }

        this.selectedRun = run;
        this.Control<StackPanel>("ResearchWelcome").IsVisible = false;
        this.Control<Grid>("RunContent").IsVisible = true;
        this.Control<TextBlock>("RunTitle").Text = run.Question;
        this.Control<TextBlock>("RunProgress").Text = $"{new ResearchRunRow(run.Summarize()).State} · {run.Findings.Count} sources · "
            + $"{run.PagesAttempted} reads · {run.ReferencesConsidered} references · {run.Issues.Count} skipped";
        this.Control<SelectableTextBlock>("RunTrace").Text = $"{run.StartedAt.ToLocalTime():MMM d, yyyy · h:mm tt} · Trace {run.TraceId:D}";
        this.Control<TextBlock>("AnswerStatus").Text = ResearchPresentation.AnswerLabel(run);
        this.Control<ReplyView>("ResearchAnswer").Text = run.Answer ?? "";
        this.Control<Button>("CopyReport").IsEnabled = true;
        this.ShowNotices(run);
        this.ShowSources(run);
    }

    private void ShowNotices(ResearchRun run)
    {
        var notes = new List<string>();
        if (run.CurrentUrl is { } url) { notes.Add($"Reading {url}"); }
        if (run.PageLimitReached) { notes.Add("Page limit reached; additional references remain unread."); }
        if (run.Error is { } error) { notes.Add(error); }
        if (run.Status != ResearchRunStatus.Running && run.Findings.Count == 0)
        {
            notes.Add("No sources could be collected. Use Edit and rerun to revise the question, try another starting URL, or leave it empty for Dami to choose sites.");
        }
        else if (run.AnswerStatus == ResearchAnswerStatus.None)
        {
            notes.Add(run.Status == ResearchRunStatus.Running ? "Source collection is in progress."
                : "Sources are available in the inspector. If the reply is still streaming, its answer will appear when it ends.");
        }

        this.Control<SelectableTextBlock>("RunNotice").Text = string.Join("\n\n", notes);
        this.Control<Border>("RunNoticePanel").IsVisible = notes.Count > 0;
        this.Control<SelectableTextBlock>("SkippedSources").Text = string.Join("\n\n",
            run.Issues.Select(issue => $"Skipped {issue.Url}\n{issue.Reason}"));
    }

    private void ShowSources(ResearchRun run)
    {
        var choice = this.Control<ComboBox>("SourceChoice");
        var previous = (choice.SelectedItem as ResearchSourceRow)?.Finding.Page.Url;
        var rows = run.Findings.Select((finding, index) => new ResearchSourceRow(finding, index)).ToArray();
        this.Control<ItemsControl>("SourceCards").ItemsSource = rows;
        choice.ItemsSource = rows;
        choice.SelectedItem = rows.FirstOrDefault(row => row.Finding.Page.Url == previous) ?? rows.FirstOrDefault();
        this.ShowSource();
    }

    private void ShowSource()
    {
        var row = this.Control<ComboBox>("SourceChoice").SelectedItem as ResearchSourceRow;
        this.Control<Button>("CopySource").IsEnabled = row is not null;
        this.Control<SelectableTextBlock>("SourceText").Text = row?.Finding.Page.Text ?? "No retained source text for this run.";
        this.Control<SelectableTextBlock>("SourceMeta").Text = row is null ? "" :
            $"{row.Finding.Page.Url}\nHTTP {row.Finding.Page.StatusCode} · depth {row.Finding.Depth} · {row.Finding.Page.Text.Length:N0} characters\nDiscovery path · click a step to open its original page:";
        this.Control<ItemsControl>("SourcePath").ItemsSource = row?.Finding.Path.Select((url, index) => new ResearchPathRow(url, index + 1)).ToArray();
    }

    internal void OnContentClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button { Tag: "copy-code", CommandParameter: string code })
        {
            _ = this.CopyAsync(code, "Code copied.");
            e.Handled = true;
        }
        else if (e.Source is Button { Tag: "inspect-source", CommandParameter: int index })
        {
            this.Control<TabItem>("SourcesTab").IsSelected = true;
            this.Control<ComboBox>("SourceChoice").SelectedIndex = index;
            e.Handled = true;
        }
        else if (e.Source is Button { Tag: "open-source", CommandParameter: Uri url })
        {
            _ = this.OpenSourceAsync(url);
            e.Handled = true;
        }
    }

    private async Task CopyAsync(string? text, string message)
    {
        try
        {
            if (text is not null && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text).ConfigureAwait(true);
                this.Control<TextBlock>("ResearchSync").Text = message;
            }
        }
        catch (Exception exception) { this.Control<TextBlock>("ResearchSync").Text = $"Copy failed: {exception.Message}"; }
    }

    private async Task OpenSourceAsync(Uri url)
    {
        try
        {
            if (url.IsAbsoluteUri && url.Scheme is "http" or "https" && TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
            {
                var opened = await launcher.LaunchUriAsync(url).ConfigureAwait(true);
                this.Control<TextBlock>("ResearchSync").Text = opened ? "Source opened in your browser." : "The browser could not open this source.";
            }
        }
        catch (Exception exception) { this.Control<TextBlock>("ResearchSync").Text = $"Open failed: {exception.Message}"; }
    }
}

/// <summary>A compact history card.</summary>
public sealed record ResearchRunRow(ResearchRunSummary Run)
{
    public string Question => this.Run.Question;
    public string State => this.Run.Status == ResearchRunStatus.Completed && this.Run.SourceCount == 0
        ? "No sources collected" : StateLabel(this.Run.Status);
    public string Details => $"{this.Run.StartedAt.ToLocalTime():MMM d · h:mm tt} · {this.Run.SourceCount} sources";
    public static string StateLabel(ResearchRunStatus status) => status switch
    {
        ResearchRunStatus.Running => "Collecting sources",
        ResearchRunStatus.Completed => "Sources collected",
        _ => status.ToString(),
    };
}

/// <summary>A saved source with a readable preview.</summary>
public sealed record ResearchSourceRow(ResearchFinding Finding, int Index)
{
    public string Title => $"{this.Index + 1:00}  " + (string.IsNullOrWhiteSpace(this.Finding.Page.Title)
        ? this.Finding.Page.Url.Host + this.Finding.Page.Url.PathAndQuery : this.Finding.Page.Title);
    public string Detail => $"{this.Finding.Page.Url.Host} · depth {this.Finding.Depth} · inspect source →";
    public string Preview => this.Finding.Page.Text[..Math.Min(260, this.Finding.Page.Text.Length)].Replace('\n', ' ');
}

/// <summary>A clickable original URL in a discovery path.</summary>
public sealed record ResearchPathRow(Uri Url, int Step)
{
    public string Label => $"{this.Step} · {this.Url.Host} ↗";
}
