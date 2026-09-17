using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Dami.Gui;

/// <summary>Conversation request control and recovery of an unsuccessful submission.</summary>
public sealed partial class MainWindow
{
    private readonly ChatTurnController chatTurns = new();

    private ChatDraft CaptureDraft() => new(
        this.input.Text ?? string.Empty,
        this.state.PendingImages.Select(item => item.Request).ToArray());

    private async Task SendAsync()
    {
        var draft = this.CaptureDraft();
        var text = ChatImageInput.MessageOrDefault(draft.Text, draft.Images.Count > 0);
        if (text is null)
        {
            return;
        }

        await this.chatTurns.RunAsync(
            token => this.SendCoreAsync(draft, text, token), this.lifetime.Token).ConfigureAwait(true);
    }

    private async Task SendCoreAsync(ChatDraft draft, string text, CancellationToken cancellationToken)
    {
        var reply = this.BeginReply(text);
        try
        {
            await this.AnswerAsync(reply, text, draft.Images, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            reply.Meta = "stopped";
            reply.RecoverableDraft = draft;
            this.SetStatus(new GlobalStatus("STOPPED", "Reply stopped. Restore your message below.", false));
        }
        catch (Exception exception)
        {
            reply.Meta = $"failed: {exception.Message}";
            reply.RecoverableDraft = draft;
            this.SetStatus(GlobalStatus.Failure(exception.Message));
        }
        finally
        {
            this.sendButton.Content = "send";
            this.sendButton.IsEnabled = true;
            ToolTip.SetTip(this.sendButton, "Send your message (Enter)");
        }
    }

    private Message BeginReply(string text)
    {
        var images = this.state.PendingImages.ToArray();
        this.input.Text = string.Empty;
        this.state.PendingImages.Clear();
        this.sendButton.Content = "stop";
        ToolTip.SetTip(this.sendButton, "Stop the current reply (Esc)");
        this.SetStatus(GlobalStatus.Working("Message sent to Dami…"));
        this.input.Focus();
        return this.OpenExchange(text, images);
    }

    private void StopReply()
    {
        this.sendButton.Content = "stopping…";
        this.sendButton.IsEnabled = false;
        this.SetStatus(GlobalStatus.Working("Stopping the reply…"));
        this.chatTurns.Stop();
    }

    private void OnChatKeyDown(object? sender, KeyEventArgs e)
    {
        if (this.HandleImageKey(e))
        {
            return;
        }

        if (this.HandleWorkspaceKey(e))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && this.chatTurns.IsRunning)
        {
            e.Handled = true;
            this.StopReply();
        }
    }

    private void OnConversationClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button button)
        {
            return;
        }

        if (button is { Tag: "recover", DataContext: Message { RecoverableDraft: { } draft } })
        {
            e.Handled = true;
            this.RestoreDraft(draft);
        }
        else if (button is { Tag: "starter", CommandParameter: string prompt })
        {
            e.Handled = true;
            this.RestoreDraft(new ChatDraft(prompt, []), "Starting point added. Make it yours, then press Enter.");
        }
        else if (button is { Tag: "copy", DataContext: Message message })
        {
            e.Handled = true;
            _ = this.CopyMessageAsync(message);
        }
        else if (button is { Tag: "copy-code", CommandParameter: string code })
        {
            e.Handled = true;
            _ = this.CopyTextAsync(code, "Code");
        }
    }

    private void RestoreDraft(
        ChatDraft draft, string successMessage = "Message restored. Review it and press Enter to send.")
    {
        var current = this.CaptureDraft();
        var restored = draft.Restore(current);
        if (ReferenceEquals(restored, current))
        {
            this.SetStatus(new GlobalStatus("DRAFT KEPT", "Your draft was kept. Send or clear it first.", false));
            this.input.Focus();
            return;
        }

        this.input.Text = restored.Text;
        foreach (var image in restored.Images)
        {
            this.StageImage(image);
        }

        this.input.CaretIndex = restored.Text.Length;
        this.input.Focus();
        this.SetStatus(GlobalStatus.Success(successMessage));
    }
}
