using Avalonia.Controls;
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

    private readonly RuntimeClient runtime = new();
    private readonly WindowState state = new();
    private readonly Dictionary<Guid, Guid?> spanParents = [];
    private readonly HashSet<Guid> seenTraces = [];
    private readonly CancellationTokenSource lifetime = new();

    private long lastSequence;

    /// <summary>Creates the window and starts following the event stream.</summary>
    public MainWindow()
    {
        this.InitializeComponent();
        this.DataContext = this.state;
        this.Closed += this.OnClosed;
        _ = this.FollowAsync();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
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
