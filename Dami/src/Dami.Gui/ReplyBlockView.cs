using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Dami.Gui;

/// <summary>A selectable reply section, with a dedicated copy action for literal code.</summary>
public sealed class ReplyBlockView : Border
{
    private readonly SelectableTextBlock text = new();
    private readonly Button copy = new();
    private readonly TextBlock label = new();
    private static readonly IBrush ink = Brush.Parse("#EEEEEE");
    private static readonly IBrush accent = Brush.Parse("#ABA5FF");
    private static readonly FontFamily monospace = new("DejaVu Sans Mono");

    /// <summary>Creates a view for a block kind. Updates keep that same kind.</summary>
    public ReplyBlockView(ReplyBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        this.text.Foreground = ink;
        this.text.FontSize = 15;
        this.text.LineHeight = 24;
        this.text.TextWrapping = TextWrapping.Wrap;
        if (block.Kind == "code")
        {
            this.BuildCode();
        }
        else
        {
            this.Child = this.text;
            this.StyleProse(block.Kind);
        }

        this.Update(block);
    }

    /// <summary>Updates text and code-copy payload as the reply arrives.</summary>
    public void Update(ReplyBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (block.Kind is "code" or "literal")
        {
            this.text.Text = block.Text;
            this.copy.CommandParameter = block.Text;
            this.label.Text = string.IsNullOrEmpty(block.Language) ? "CODE" : block.Language;
            return;
        }

        var inlines = new InlineCollection();
        foreach (var span in ReplyInlines.Parse(block.Text))
        {
            inlines.Add(CreateRun(span));
        }

        this.text.Inlines = inlines;
    }

    private static Run CreateRun(ReplyInline span)
    {
        var run = new Run(span.Text);
        if (span.Kind == "bold")
        {
            run.FontWeight = FontWeight.SemiBold;
        }
        else if (span.Kind == "italic")
        {
            run.FontStyle = FontStyle.Italic;
        }
        else if (span.Kind == "code")
        {
            run.FontFamily = monospace;
            run.Foreground = accent;
        }

        return run;
    }

    private void StyleProse(string kind)
    {
        if (kind.StartsWith('h'))
        {
            this.text.FontSize = kind switch { "h1" => 24, "h2" => 20, _ => 17 };
            this.text.LineHeight = this.text.FontSize + 8;
            this.text.FontWeight = FontWeight.SemiBold;
            this.Padding = new Thickness(0, 8, 0, 2);
        }
        else if (kind == "quote")
        {
            this.BorderBrush = accent;
            this.BorderThickness = new Thickness(3, 0, 0, 0);
            this.Padding = new Thickness(14, 4);
            this.text.Foreground = Brush.Parse("#C4C4C4");
        }
        else if (kind == "list")
        {
            this.Padding = new Thickness(8, 0, 0, 0);
        }
    }

    private void BuildCode()
    {
        this.Background = Brush.Parse("#171717");
        this.BorderBrush = Brush.Parse("#3A3A3A");
        this.BorderThickness = new Thickness(1);
        this.CornerRadius = new CornerRadius(9);
        this.ClipToBounds = true;
        this.text.FontFamily = monospace;
        this.text.FontSize = 13;
        this.text.LineHeight = 21;
        this.text.TextWrapping = TextWrapping.NoWrap;
        this.text.Margin = new Thickness(14, 12);
        var panel = new DockPanel();
        var header = this.BuildCodeHeader();
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(new ScrollViewer
        {
            Content = this.text,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
        this.Child = panel;
    }

    private Border BuildCodeHeader()
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        this.label.FontSize = 11;
        this.label.Foreground = accent;
        this.label.VerticalAlignment = VerticalAlignment.Center;
        this.copy.Content = "Copy code";
        this.copy.Tag = "copy-code";
        this.copy.FontSize = 11;
        this.copy.Padding = new Thickness(9, 4);
        this.copy.Classes.Add("quiet");
        ToolTip.SetTip(this.copy, "Copy the code exactly, without the surrounding message or fences.");
        Grid.SetColumn(this.copy, 1);
        header.Children.Add(this.label);
        header.Children.Add(this.copy);
        return new Border
        {
            Child = header, Padding = new Thickness(12, 7),
            Background = Brush.Parse("#292929"),
        };
    }
}
