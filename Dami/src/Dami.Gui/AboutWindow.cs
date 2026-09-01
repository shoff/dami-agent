using Avalonia.Controls;
using Avalonia.Media;

namespace Dami.Gui;

/// <summary>Small product-information window opened from the top menu.</summary>
public sealed class AboutWindow : Window
{
    /// <summary>Creates the about window.</summary>
    public AboutWindow()
    {
        this.Title = "About Dami";
        this.Width = 420;
        this.Height = 220;
        this.Background = Brush.Parse("#101418");
        this.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Dami", FontSize = 24, Foreground = Brush.Parse("#D7DDE4") },
                new TextBlock
                {
                    Text = "A continuous modeling system with a conversational surface.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brush.Parse("#7A8694"),
                },
            },
        };
    }
}
