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

    private readonly RuntimeClient runtime = new();
    private readonly WindowState state = new();
    private readonly CancellationTokenSource lifetime = new();

    private long lastSequence;

    // Resolved explicitly. A hand-written InitializeComponent that only calls
    // AvaloniaXamlLoader.Load does NOT populate x:Name fields — they stay null, and
    // every symptom is silent: the send button does nothing, the status line never
    // updates, and the poll loop dies mid-render. Look them up and fail loudly.
    private readonly TextBox input;
    private readonly Border conversationPanel;
    private readonly Button sendButton;
    private readonly ToggleButton speakToggle;
    private readonly TextBlock statusLine;
    private readonly ScrollViewer chatScroll;
    private readonly MenuItem jobsMenuItem;
    private readonly MenuItem exitMenuItem;
    private readonly MenuItem aboutMenuItem;

    /// <summary>Creates the window and starts following the event stream.</summary>
    public MainWindow()
    {
        this.InitializeComponent();
        this.input = Require<TextBox>(this, "Input");
        this.conversationPanel = Require<Border>(this, "ConversationPanel");
        this.sendButton = Require<Button>(this, "SendButton");
        this.speakToggle = Require<ToggleButton>(this, "SpeakToggle");
        this.statusLine = Require<TextBlock>(this, "StatusLine");
        this.chatScroll = Require<ScrollViewer>(this, "ChatScroll");
        this.jobsMenuItem = Require<MenuItem>(this, "JobsMenuItem");
        this.exitMenuItem = Require<MenuItem>(this, "ExitMenuItem");
        this.aboutMenuItem = Require<MenuItem>(this, "AboutMenuItem");
        this.DataContext = this.state;
        this.Closed += this.OnClosed;

        // Wired here rather than as XAML attributes. Attribute wiring depends on how the
        // XAML was compiled, and when it silently fails the symptom is a control that
        // looks alive, accepts text, and does nothing at all when you press the button.
        this.sendButton.Click += this.OnSendClick;
        this.conversationPanel.AddHandler(Button.ClickEvent, this.OnConversationClick);
        this.AddHandler(InputElement.KeyDownEvent, this.OnChatKeyDown, RoutingStrategies.Tunnel);
        this.input.AddHandler(
            InputElement.KeyDownEvent, this.OnInputKeyDown,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        this.input.PastingFromClipboard += this.OnPaste;
        DragDrop.SetAllowDrop(this.conversationPanel, true);
        DragDrop.AddDragOverHandler(this.conversationPanel, this.OnDragOver);
        DragDrop.AddDropHandler(this.conversationPanel, this.OnDrop);
        this.jobsMenuItem.Click += (_, _) => new JobsWindow(this.runtime).Show(this);
        this.exitMenuItem.Click += (_, _) => this.Close();
        this.aboutMenuItem.Click += (_, _) => _ = new AboutWindow().ShowDialog(this);
        this.InitializeWorkspace();
        this.InitializeChatScrolling();
        this.InitializeConversationFinder();
        this.InitializeTaskBoards();
        this.InitializeFitness();
        this.InitializeNetwork();
        this.InitializeGallery();
        this.InitializeImages();
        this.InitializeResearch();
        this.Opened += (_, _) => _ = this.EnsureLoggedInAsync();
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
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        if (ComposerKey.ShouldSend(e.Key, e.KeyModifiers))
        {
            _ = this.SendAsync();
            return;
        }

        var edit = ComposerKey.InsertNewline(
            this.input.Text ?? string.Empty,
            this.input.SelectionStart,
            this.input.SelectionEnd);
        this.input.Text = edit.Text;
        this.input.CaretIndex = edit.Caret;
    }

    private void OnSendClick(object? sender, RoutedEventArgs e)
    {
        if (this.chatTurns.IsRunning)
        {
            this.StopReply();
        }
        else
        {
            _ = this.SendAsync();
        }
    }

}
