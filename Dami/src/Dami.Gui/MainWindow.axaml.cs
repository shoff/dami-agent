using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Dami.Gui;

/// <summary>
/// The charter's centerpiece: conversation beside a live workflow graph. Everything
/// shown is read from the runtime's persisted event stream — nothing here is inferred,
/// which is the §7.4 trust boundary the whole design rests on.
/// </summary>
public sealed partial class MainWindow : Window
{
    private static readonly TimeSpan pollInterval = TimeSpan.FromSeconds(2);

    /// <summary>Rows drawn from one poll. A cold start must not replay the whole backlog.</summary>
    private const int MAX_ROWS_PER_POLL = 25;

    /// <summary>Rows kept on screen, so an all-day window stays responsive.</summary>
    private const int MAX_GRAPH_ROWS = 400;

    private readonly RuntimeClient runtime = new();
    private readonly WindowState state = new();
    private readonly Dictionary<Guid, Guid?> spanParents = [];
    private readonly HashSet<Guid> seenTraces = [];
    private readonly CancellationTokenSource lifetime = new();

    private long lastSequence;

    // Resolved explicitly. A hand-written InitializeComponent that only calls
    // AvaloniaXamlLoader.Load does NOT populate x:Name fields — they stay null, and
    // every symptom is silent: the send button does nothing, the status line never
    // updates, and the poll loop dies mid-render. Look them up and fail loudly.
    private readonly TextBox input;
    private readonly Button sendButton;
    private readonly ToggleButton frontierToggle;
    private readonly TextBlock statusLine;
    private readonly ScrollViewer chatScroll;
    private readonly ScrollViewer graphScroll;

    /// <summary>Creates the window and starts following the event stream.</summary>
    public MainWindow()
    {
        this.InitializeComponent();
        this.input = Require<TextBox>(this, "Input");
        this.sendButton = Require<Button>(this, "SendButton");
        this.frontierToggle = Require<ToggleButton>(this, "FrontierToggle");
        this.statusLine = Require<TextBlock>(this, "StatusLine");
        this.chatScroll = Require<ScrollViewer>(this, "ChatScroll");
        this.graphScroll = Require<ScrollViewer>(this, "GraphScroll");
        this.DataContext = this.state;
        this.Closed += this.OnClosed;

        // Wired here rather than as XAML attributes. Attribute wiring depends on how the
        // XAML was compiled, and when it silently fails the symptom is a control that
        // looks alive, accepts text, and does nothing at all when you press the button.
        this.sendButton.Click += this.OnSendClick;
        this.input.KeyDown += this.OnInputKeyDown;
        _ = this.FollowAsync();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Finds a named control, refusing to start if the name is wrong.</summary>
    private static T Require<T>(Window window, string name)
        where T : Control
    {
        return window.FindControl<T>(name)
            ?? throw new InvalidOperationException(
                $"The window has no {typeof(T).Name} named '{name}'.");
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        this.lifetime.Cancel();
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = this.SendAsync();
        }
    }

    private void OnSendClick(object? sender, RoutedEventArgs e)
    {
        _ = this.SendAsync();
    }
}
