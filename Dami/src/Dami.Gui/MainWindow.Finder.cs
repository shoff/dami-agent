using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Dami.Gui;

/// <summary>Local message search and keyboard navigation within the open conversation.</summary>
public sealed partial class MainWindow
{
    private readonly ConversationFinder finder = new();
    private Border conversationFindBar = null!;
    private TextBox conversationFindInput = null!;
    private TextBlock conversationFindCount = null!;
    private ItemsControl messageList = null!;
    private Border? foundMessage;

    private void InitializeConversationFinder()
    {
        this.conversationFindBar = Require<Border>(this, "ConversationFindBar");
        this.conversationFindInput = Require<TextBox>(this, "ConversationFindInput");
        this.conversationFindCount = Require<TextBlock>(this, "ConversationFindCount");
        this.messageList = Require<ItemsControl>(this, "MessageList");
        Require<Button>(this, "FindConversation").Click += (_, _) => this.OpenConversationFind();
        Require<Button>(this, "ConversationFindClose").Click += (_, _) => this.CloseConversationFind();
        Require<Button>(this, "ConversationFindNext").Click += (_, _) => this.MoveConversationFind(1);
        Require<Button>(this, "ConversationFindPrevious").Click += (_, _) => this.MoveConversationFind(-1);
        this.conversationFindInput.TextChanged += (_, _) => this.RefreshConversationFind(true);
        this.state.Messages.CollectionChanged += (_, args) =>
        {
            if (args.NewItems is not null)
            {
                foreach (Message message in args.NewItems)
                {
                    message.PropertyChanged += this.OnSearchMessageChanged;
                }
            }

            this.RefreshConversationFind(false);
        };
    }

    private void OnSearchMessageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(Message.Body))
        {
            this.RefreshConversationFind(false);
        }
    }

    private void OpenConversationFind()
    {
        this.workspaceTabs.SelectedIndex = 0;
        this.conversationFindBar.IsVisible = true;
        this.RefreshConversationFind(true);
        this.conversationFindInput.Focus();
        this.conversationFindInput.SelectAll();
    }

    private void CloseConversationFind()
    {
        this.conversationFindBar.IsVisible = false;
        this.foundMessage?.Classes.Remove("search-hit");
        this.foundMessage = null;
        this.input.Focus();
    }

    private void RefreshConversationFind(bool navigate)
    {
        if (!this.conversationFindBar.IsVisible)
        {
            return;
        }

        this.finder.Search(this.state.Messages, this.conversationFindInput.Text);
        this.UpdateConversationFindStatus();
        if (navigate && this.finder.Selected is not null)
        {
            Dispatcher.UIThread.Post(this.ShowFoundMessage, DispatcherPriority.Background);
        }
    }

    private void MoveConversationFind(int direction)
    {
        this.finder.Move(direction);
        this.UpdateConversationFindStatus();
        this.ShowFoundMessage();
    }

    private void UpdateConversationFindStatus()
    {
        this.conversationFindCount.Text = string.IsNullOrWhiteSpace(this.conversationFindInput.Text)
            ? "Find a message" : this.finder.Count == 0 ? "No matches"
            : $"{this.finder.Position + 1} / {this.finder.Count} messages";
        Require<Button>(this, "ConversationFindPrevious").IsEnabled = this.finder.Count > 0;
        Require<Button>(this, "ConversationFindNext").IsEnabled = this.finder.Count > 0;
        if (this.finder.Selected is null)
        {
            this.foundMessage?.Classes.Remove("search-hit");
            this.foundMessage = null;
        }
    }

    private void ShowFoundMessage()
    {
        if (!this.conversationFindBar.IsVisible || this.finder.Selected is not { } message)
        {
            return;
        }

        var index = this.state.Messages.IndexOf(message);
        if (this.messageList.ContainerFromIndex(index) is not { } container)
        {
            return;
        }

        this.foundMessage?.Classes.Remove("search-hit");
        this.foundMessage = container.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(border => border.Classes.Contains("message"));
        this.foundMessage?.Classes.Add("search-hit");
        this.chatFollow.Pause();
        this.jumpToLatest.IsVisible = true;
        container.BringIntoView(new Rect(0, 0, container.Bounds.Width, this.chatScroll.Viewport.Height));
    }

    private bool HandleConversationFindKey(KeyEventArgs args)
    {
        if (args.Key == Key.F && args.KeyModifiers == KeyModifiers.Control)
        {
            this.OpenConversationFind();
            return true;
        }

        if (!this.conversationFindBar.IsVisible || this.workspaceTabs.SelectedIndex != 0)
        {
            return false;
        }

        if (args.Key == Key.Escape)
        {
            this.CloseConversationFind();
            return true;
        }

        if (args.Key == Key.Enter && this.conversationFindInput.IsFocused
            && args.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
        {
            this.MoveConversationFind(args.KeyModifiers == KeyModifiers.Shift ? -1 : 1);
            return true;
        }

        return false;
    }
}
