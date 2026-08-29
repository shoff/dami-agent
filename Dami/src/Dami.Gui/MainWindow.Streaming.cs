using System.Text.Json;

namespace Dami.Gui;

/// <summary>The window's behaviour: send a turn, and follow the event stream.</summary>
public sealed partial class MainWindow
{
    private async Task SendAsync()
    {
        var text = this.input.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var frontier = this.frontierToggle.IsChecked == true;
        this.input.Text = string.Empty;
        this.sendButton.IsEnabled = false;
        var reply = this.OpenExchange(text, frontier);

        try
        {
            await this.AnswerAsync(reply, text, frontier).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            // Every exception, not a chosen few. A silent no-op is the worst failure a
            // send button can have — it is indistinguishable from a dead control.
            reply.Meta = $"failed: {exception.Message}";
        }
        finally
        {
            this.sendButton.IsEnabled = true;
        }
    }

    /// <summary>Puts the question and an empty reply on screen before the model answers.</summary>
    private Message OpenExchange(string text, bool frontier)
    {
        this.state.Messages.Add(new Message("you", text));
        var reply = new Message("dami", string.Empty)
        {
            Meta = frontier ? "asking your subscription…" : "thinking…",
        };
        this.state.Messages.Add(reply);
        ScrollLater(this.chatScroll);
        return reply;
    }

    /// <summary>Routes the turn to the subscription or the local sidecar.</summary>
    private Task AnswerAsync(Message reply, string text, bool frontier)
    {
        return frontier
            ? this.AskFrontierAsync(reply, text)
            : this.StreamIntoAsync(reply, text);
    }

    /// <summary>
    /// Answers on the ChatGPT subscription through the codex CLI (ADR-0011). No API key
    /// is involved and no retrieved memory is sent — that is what keeps it Egressable
    /// without a consent step. For a memory-informed frontier answer, use `dami brief`.
    /// </summary>
    private async Task AskFrontierAsync(Message reply, string text)
    {
        using var answer = await this.runtime.PostAsync(
            "/turns", new { message = text, frontier = true }, this.lifetime.Token)
            .ConfigureAwait(true);
        if (answer is null)
        {
            reply.Meta = "the runtime is unreachable";
            return;
        }

        var root = answer.RootElement;
        if (root.TryGetProperty("refused", out var refused))
        {
            reply.Meta = $"refused: {refused.GetString()}";
            return;
        }

        reply.Body = root.GetProperty("answer").GetString() ?? string.Empty;
        reply.Meta = $"frontier · subscription · trace "
            + $"{root.GetProperty("traceId").GetGuid().ToString("N")[..8]}";
        ScrollLater(this.chatScroll);
    }

    private async Task StreamIntoAsync(Message reply, string text)
    {
        var any = false;
        await foreach (var fragment in this.runtime
            .StreamTurnAsync(text, this.lifetime.Token).ConfigureAwait(true))
        {
            any = true;
            reply.Body += fragment;
            reply.Meta = string.Empty;
            ScrollLater(this.chatScroll);
        }

        if (!any)
        {
            reply.Meta = "the runtime returned nothing";
        }
    }

    private async Task FollowAsync()
    {
        while (!this.lifetime.IsCancellationRequested)
        {
            try
            {
                await this.PollOnceAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                // A poll that throws must not end the follow loop. The first version of
                // this let one bad event kill the stream silently: the graph rendered a
                // single row and then simply stopped, looking like an idle system.
                this.statusLine.Text = $"poll failed: {exception.GetType().Name}: {exception.Message}";
                Diagnostics.Write($"poll failed: {exception}");
            }

            try
            {
                await Task.Delay(pollInterval, this.lifetime.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task PollOnceAsync()
    {
        Diagnostics.Write($"poll starting from seq {this.lastSequence}");
        using var events = await this.runtime
            .GetAsync($"/events?after={this.lastSequence}", this.lifetime.Token).ConfigureAwait(true);
        if (events is null)
        {
            Diagnostics.Write("poll: /events returned null (unreachable or unparseable)");
            this.statusLine.Text = "dami-host unreachable";
            return;
        }

        // Render only the tail of a batch. A cold start returns the whole backlog, and
        // the first version added every row individually while forcing a scroll — a
        // layout pass per row, quadratic, and it froze the window solid. "Live" means
        // recent activity, not the entire history replayed at startup.
        var batch = events.RootElement.EnumerateArray().ToList();
        this.AdvanceSequence(batch);

        this.statusLine.Text = $"live · seq {this.lastSequence}";
        Diagnostics.Write($"poll ok: {batch.Count} event(s), seq {this.lastSequence}");
        await this.RefreshSidebarsAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Scrolls once the layout has settled. Calling ScrollToEnd inline forces a layout
    /// pass from inside one, and the pass never completes — the symptom is a window
    /// that paints its rows and then stops responding entirely.
    /// </summary>
    private static void ScrollLater(Avalonia.Controls.ScrollViewer viewer)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(
            viewer.ScrollToEnd, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>Keeps the sequence honest even for rows the tail-window skipped.</summary>
    private void AdvanceSequence(List<JsonElement> batch)
    {
        foreach (var item in batch)
        {
            this.lastSequence = Math.Max(this.lastSequence, item.GetProperty("sequence").GetInt64());
        }
    }

}
