using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Dami.Gui;

/// <summary>Right-click anything and ask Dami about it.</summary>
/// <remarks>
/// The point is that the question arrives with context already attached. Asking "why is
/// this red" in the conversation pane requires describing what "this" is, and by the time
/// that is typed out the question has usually answered itself. Right-clicking the red
/// thing skips all of it.
///
/// What gets sent is the view model behind the control, not the control: a TextBlock tells
/// a model nothing, while the pass, service, or surfacing behind it says everything. The
/// walk up the visual tree exists for the same reason — a click usually lands on a run of
/// text several levels below the object that gives it meaning.
/// </remarks>
public sealed partial class MainWindow
{
    private Border askPopup = null!;
    private TextBox askInput = null!;
    private TextBlock askSubject = null!;
    private TextBlock askAnswer = null!;
    private string askContext = string.Empty;

    private void InitialiseAsk()
    {
        this.askPopup = Require<Border>(this, "AskPopup");
        this.askInput = Require<TextBox>(this, "AskInput");
        this.askSubject = Require<TextBlock>(this, "AskSubject");
        this.askAnswer = Require<TextBlock>(this, "AskAnswer");

        Require<Button>(this, "AskButton").Click += this.OnAsk;
        Require<Button>(this, "AskClose").Click += (_, _) => this.askPopup.IsVisible = false;
        this.askInput.KeyDown += this.OnAskKey;

        // Tunnel, so the question can be asked about a control that handles its own right
        // click. Bubbling would never reach here for those.
        this.AddHandler(
            InputElement.PointerPressedEvent, this.OnPointerRightPressed, RoutingStrategies.Tunnel);
    }

    private void OnPointerRightPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            return;
        }

        this.askContext = AskContext.Describe(ModelUnder(e.Source), VisibleText(e.Source));
        this.askSubject.Text = this.askContext.Length == 0
            ? "nothing identifiable was under the pointer"
            : this.askContext;
        this.askAnswer.Text = string.Empty;
        this.askInput.Text = string.Empty;
        this.askPopup.Margin = this.Anchor(e.GetPosition(this));
        this.askPopup.IsVisible = true;
        this.askInput.Focus();
        e.Handled = true;
    }

    /// <summary>Places the panel at the pointer, kept fully on screen.</summary>
    /// <remarks>
    /// Clamped rather than allowed to run off the edge: a question box half outside the
    /// window is one you cannot finish typing in, and the pointer is often near an edge
    /// precisely because that is where the interesting rows are.
    /// </remarks>
    private Thickness Anchor(Point pointer)
    {
        const double width = 460;
        const double height = 260;
        var left = Math.Max(8, Math.Min(pointer.X + 12, this.Bounds.Width - width - 8));
        var top = Math.Max(8, Math.Min(pointer.Y + 12, this.Bounds.Height - height - 8));
        return new Thickness(left, top, 0, 0);
    }

    /// <remarks>
    /// Walks up until it finds a DataContext that is one of the application's own models.
    /// Stopping at the first non-null DataContext would usually find the window's own
    /// state, which describes everything and therefore nothing.
    /// </remarks>
    private static object? ModelUnder(object? source)
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Control { DataContext: { } model } && IsOwnModel(model))
            {
                return model;
            }
        }

        return null;
    }

    private static bool IsOwnModel(object model) =>
        model is WorkerRow or WorkerRun or PassEvent or SidebarItem or ActivitySeries or Message
            or TaskBoardTaskNode or TaskBoardCriterionNode;

    private static string VisibleText(object? source) =>
        source is TextBlock { Text: { } text } ? text : string.Empty;

    private void OnAskKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = this.AskAsync();
        }
    }

    private void OnAsk(object? sender, RoutedEventArgs e) => _ = this.AskAsync();

    private async Task AskAsync()
    {
        var question = this.askInput.Text?.Trim();
        if (string.IsNullOrEmpty(question))
        {
            return;
        }

        this.askAnswer.Text = "thinking…";
        try
        {
            using var reply = await this.runtime.PostAsync(
                "/turns",
                new { message = AskContext.Prompt(this.askContext, question) },
                this.lifetime.Token).ConfigureAwait(true);

            this.askAnswer.Text = reply?.RootElement.TryGetProperty("answer", out var answer) is true
                ? answer.GetString() ?? "(the runtime returned nothing)"
                : "the runtime could not answer that";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.askAnswer.Text = $"could not ask: {exception.Message}";
        }
    }
}
