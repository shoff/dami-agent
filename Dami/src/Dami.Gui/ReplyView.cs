using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace Dami.Gui;

/// <summary>Displays reply structure while retaining unchanged controls during streaming.</summary>
public sealed class ReplyView : StackPanel
{
    /// <summary>The original reply body.</summary>
    public static readonly StyledProperty<string> textProperty =
        AvaloniaProperty.Register<ReplyView, string>(nameof(Text), string.Empty);

    private IReadOnlyList<ReplyBlock> blocks = [];

    /// <summary>Creates a vertically spaced reply reader.</summary>
    public ReplyView()
    {
        this.Spacing = 10;
        // Bind directly so the registered field follows the repository's camelCase rule.
        this.Bind(textProperty, new Binding(nameof(Message.Body)) { Mode = BindingMode.OneWay });
    }

    /// <summary>Gets or sets the original reply body.</summary>
    public string Text
    {
        get => this.GetValue(textProperty);
        set => this.SetValue(textProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == textProperty)
        {
            this.Refresh();
        }
    }

    private void Refresh()
    {
        var next = ReplyDocument.Parse(this.Text ?? string.Empty);
        while (this.Children.Count > next.Count)
        {
            this.Children.RemoveAt(this.Children.Count - 1);
        }

        for (var index = 0; index < next.Count; index++)
        {
            this.UpdateBlock(index, next[index]);
        }

        this.blocks = next;
    }

    private void UpdateBlock(int index, ReplyBlock block)
    {
        if (index >= this.Children.Count)
        {
            this.Children.Add(new ReplyBlockView(block));
        }
        else if (this.blocks[index] != block)
        {
            if (this.blocks[index].Kind == block.Kind)
            {
                ((ReplyBlockView)this.Children[index]).Update(block);
            }
            else
            {
                this.Children[index] = new ReplyBlockView(block);
            }
        }
    }
}
