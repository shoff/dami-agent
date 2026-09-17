using Avalonia.Controls;
using Avalonia.Threading;

namespace Dami.Gui;

/// <summary>Follows live replies until the reader chooses an earlier part of the conversation.</summary>
public sealed partial class MainWindow
{
    private readonly ChatScrollFollow chatFollow = new();
    private Button jumpToLatest = null!;
    private bool chatScrollQueued;

    private void InitializeChatScrolling()
    {
        this.jumpToLatest = Require<Button>(this, "JumpToLatest");
        this.jumpToLatest.Click += (_, _) =>
        {
            this.ResumeChatScroll();
            this.chatScroll.Focus();
        };
        this.chatScroll.ScrollChanged += this.OnChatScrollChanged;
    }

    private void OnChatScrollChanged(object? sender, ScrollChangedEventArgs args)
    {
        var layoutChanged = args.ExtentDelta.Y != 0 || args.ViewportDelta.Y != 0;
        this.chatFollow.Observe(
            this.chatScroll.Offset.Y, this.chatScroll.Extent.Height,
            this.chatScroll.Viewport.Height, layoutChanged,
            this.conversationFindBar?.IsVisible == true && this.finder.Selected is not null);
        this.jumpToLatest.IsVisible = !this.chatFollow.IsFollowing;
        if (layoutChanged)
        {
            this.QueueChatScroll();
        }
    }

    private void ResumeChatScroll()
    {
        if (this.conversationFindBar?.IsVisible == true)
        {
            this.CloseConversationFind();
        }

        this.chatFollow.Resume();
        this.jumpToLatest.IsVisible = false;
        this.QueueChatScroll();
    }

    private void QueueChatScroll()
    {
        if (this.chatScrollQueued || !this.chatFollow.IsFollowing)
        {
            return;
        }

        this.chatScrollQueued = true;
        Dispatcher.UIThread.Post(this.ApplyChatScroll, DispatcherPriority.Background);
    }

    private void ApplyChatScroll()
    {
        this.chatScrollQueued = false;
        // Layout must settle first, and a scroll gesture since queuing takes precedence.
        if (this.chatFollow.IsFollowing && !this.lifetime.IsCancellationRequested
            && this.chatScroll.Viewport.Height > 0)
        {
            this.chatScroll.ScrollToEnd();
        }
    }
}
