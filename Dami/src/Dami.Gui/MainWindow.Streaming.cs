using System.Text.Json;
using Avalonia.Media.Imaging;

namespace Dami.Gui;

/// <summary>The window's behaviour: send a turn, and follow the event stream.</summary>
public sealed partial class MainWindow
{
    /// <summary>Puts the question and an empty reply on screen before the model answers.</summary>
    private Message OpenExchange(string text, IReadOnlyList<PendingChatImage> images)
    {
        this.state.Messages.Add(new Message("you", text, images)
        {
            Meta = images.Count == 0 ? string.Empty : $"attached {images.Count} image(s)",
        });
        var reply = new Message("dami", string.Empty)
        {
            Meta = DirectChatPresentation.PENDING_META,
        };
        this.state.Messages.Add(reply);
        this.ResumeChatScroll();
        return reply;
    }

    /// <summary>Routes the turn to the subscription or the requested image generator.</summary>
    private Task AnswerAsync(
        Message reply, string text, IReadOnlyList<DirectChatImage> images, CancellationToken cancellationToken)
    {
        var damiScene = ImageGenerationPrompt.DamiScene(text);
        if (damiScene is not null)
        {
            return this.GenerateDamiImageAsync(reply, damiScene, cancellationToken);
        }

        var imagePrompt = ImageGenerationPrompt.Extract(text);
        if (imagePrompt is not null)
        {
            return this.GenerateImageAsync(reply, imagePrompt, cancellationToken);
        }

        return this.StreamIntoAsync(reply, text, images, cancellationToken);
    }

    private async Task GenerateImageAsync(Message reply, string prompt, CancellationToken cancellationToken)
    {
        this.SetStatus(GlobalStatus.Working("Creating your image…"));
        using var response = await this.runtime.PostAsync(
            "/images/generate", new { prompt }, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        if (response is null)
        {
            throw new InvalidOperationException("the runtime is unreachable");
        }

        var root = response.RootElement;
        ThrowImageError(root);
        var bytes = root.GetProperty("bytes").GetBytesFromBase64();
        reply.Image = new Bitmap(new MemoryStream(bytes));
        reply.Body = "Here’s the image.";
        reply.Meta = root.GetProperty("fileName").GetString() ?? string.Empty;
        this.SetStatus(GlobalStatus.Success("Image received."));
        this.QueueChatScroll();
    }

    private async Task GenerateDamiImageAsync(Message reply, string scene, CancellationToken cancellationToken)
    {
        this.SetStatus(GlobalStatus.Working("Creating a new picture of Dami…"));
        using var response = await this.runtime.PostAsync(
            "/gallery/generate", new { prompt = scene }, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        if (response is null)
        {
            throw new InvalidOperationException("the runtime is unreachable");
        }

        var root = response.RootElement;
        ThrowImageError(root);
        var fileName = root.GetProperty("fileName").GetString()
            ?? throw new InvalidOperationException("the runtime returned no image filename");
        reply.Image = await this.LoadGalleryBitmapAsync(fileName, cancellationToken).ConfigureAwait(true);
        reply.Body = "For you. 😉";
        reply.Meta = fileName;
        this.SetStatus(GlobalStatus.Success("Dami's new portrait arrived and was saved."));
        this.QueueChatScroll();
    }

    private static void ThrowImageError(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException(error.GetString() ?? "image generation failed");
        }
    }

    /// <summary>
    /// The frontier answers on context this host retrieved and gated (ADR-0026).
    /// </summary>
    /// <remarks>
    /// This used to send `frontier: true`, which is identity-plus-question with **no
    /// retrieved memory** (ADR-0010). That left the console with two bad options and no
    /// good one: a local model that knows Steve and cannot think, or a frontier model that
    /// can think and knows nothing about him. The augmented path is the one that was built
    /// for this — local retrieval, the disclosure gate, then the frontier writes the
    /// answer — and the desktop client simply never used it.
    ///
    /// Not streamed, because the gate has to see the whole context before any of it may
    /// leave. A slower good answer beats an instant useless one.
    /// </remarks>
    private async Task AskFrontierAsync(Message reply, string text)
    {
        using var answer = await this.runtime.PostAsync(
            "/turns", new { message = text, augmented = true }, this.lifetime.Token)
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
        reply.Meta = $"frontier on {root.GetProperty("memories").GetInt32()} gated local item(s) · trace "
            + $"{root.GetProperty("traceId").GetGuid().ToString("N")[..8]}";
        this.QueueChatScroll();
        await this.SpeakAsync(reply, this.lifetime.Token).ConfigureAwait(true);
    }

    private async Task StreamIntoAsync(
        Message reply, string text, IReadOnlyList<DirectChatImage> images, CancellationToken cancellationToken)
    {
        var any = false;
        await foreach (var fragment in this.runtime
            .StreamTurnAsync(text, images, cancellationToken).ConfigureAwait(true))
        {
            if (!any)
            {
                this.SetStatus(GlobalStatus.Working("Dami is replying…"));
            }

            any = true;
            await this.ApplyAsync(reply, fragment, cancellationToken).ConfigureAwait(true);
            reply.Meta = string.Empty;
            this.QueueChatScroll();
        }

        if (!any)
        {
            throw new InvalidOperationException("the runtime returned nothing");
        }

        this.SetStatus(GlobalStatus.Success("Reply received."));
        await this.SpeakAsync(reply, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Reads a finished reply aloud, when asked to. Off by default: a machine that starts
    /// talking unbidden is a machine you turn off. Failures land in the reply's own meta
    /// line rather than anywhere louder — the answer already succeeded, and not being able
    /// to say it out loud does not undo that.
    /// </summary>
    /// <summary>Text appends; a picture the frontier made is fetched from the Gallery.</summary>
    private async Task ApplyAsync(Message reply, StreamedFragment fragment, CancellationToken cancellationToken)
    {
        if (fragment.PictureFileName is { } picture)
        {
            reply.Image = await this.LoadGalleryBitmapAsync(picture, cancellationToken).ConfigureAwait(true);
        }
        else
        {
            reply.Body += fragment.Text;
        }
    }

    private Task SpeakAsync(Message reply) => this.SpeakAsync(reply, this.lifetime.Token);

    private async Task SpeakAsync(Message reply, CancellationToken cancellationToken)
    {
        if (this.speakToggle.IsChecked != true || string.IsNullOrWhiteSpace(reply.Body))
        {
            return;
        }

        try
        {
            using var spoken = await this.runtime.PostAsync(
                "/speak", new { text = reply.Body }, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            if (spoken?.RootElement.TryGetProperty("audioBase64", out var encoded) is not true)
            {
                reply.Meta = $"{reply.Meta} · could not be spoken".TrimStart(' ', '·');
                return;
            }

            var failure = await Speech
                .PlayAsync(Convert.FromBase64String(encoded.GetString() ?? string.Empty), cancellationToken)
                .ConfigureAwait(true);
            if (failure is not null)
            {
                reply.Meta = $"{reply.Meta} · not spoken: {failure}".TrimStart(' ', '·');
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            reply.Meta = $"{reply.Meta} · not spoken: {exception.Message}".TrimStart(' ', '·');
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
                this.SetStatus(GlobalStatus.Failure($"Event polling failed: {exception.Message}"));
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
        await this.RefreshObservabilityAsync().ConfigureAwait(true);
        await this.RefreshSidebarsAsync().ConfigureAwait(true);
        if (this.workspaceTabs.SelectedIndex == 7)
        {
            await this.researchWorkspace.RefreshAsync().ConfigureAwait(true);
        }
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
