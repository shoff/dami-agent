using System.Text.Json;

namespace Dami.Gui;

/// <summary>The window's behaviour: send a turn, and follow the event stream.</summary>
public sealed partial class MainWindow
{
    private async Task SendAsync()
    {
        var text = this.Input.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        this.Input.Text = string.Empty;
        this.state.Messages.Add(new Message("you", text));
        var reply = new Message("dami", string.Empty);
        this.state.Messages.Add(reply);

        try
        {
            await foreach (var fragment in this.runtime
                .StreamTurnAsync(text, this.lifetime.Token).ConfigureAwait(true))
            {
                reply.Body += fragment;
                this.Refresh(reply);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            reply.Meta = $"the runtime is unreachable: {exception.Message}";
            this.Refresh(reply);
        }
    }

    /// <summary>
    /// Re-seats the message so the list rebinds. Cruder than INotifyPropertyChanged and
    /// deliberately so: one streaming reply at a time does not justify the ceremony.
    /// </summary>
    private void Refresh(Message reply)
    {
        var index = this.state.Messages.IndexOf(reply);
        if (index >= 0)
        {
            this.state.Messages[index] = reply;
            this.ChatScroll.ScrollToEnd();
        }
    }

    private async Task FollowAsync()
    {
        while (!this.lifetime.IsCancellationRequested)
        {
            await this.PollOnceAsync().ConfigureAwait(true);
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
        using var events = await this.runtime
            .GetAsync($"/events?after={this.lastSequence}", this.lifetime.Token).ConfigureAwait(true);
        if (events is null)
        {
            this.StatusLine.Text = "dami-host unreachable";
            return;
        }

        foreach (var item in events.RootElement.EnumerateArray())
        {
            this.AddGraphRow(item);
        }

        this.StatusLine.Text = $"live · seq {this.lastSequence}";
        await this.RefreshSidebarsAsync().ConfigureAwait(true);
    }

    private void AddGraphRow(JsonElement item)
    {
        this.lastSequence = Math.Max(this.lastSequence, item.GetProperty("sequence").GetInt64());
        var spanId = item.GetProperty("spanId").GetGuid();
        var parent = item.GetProperty("parentSpanId");
        this.spanParents.TryAdd(spanId, parent.ValueKind == JsonValueKind.Null ? null : parent.GetGuid());

        var traceId = item.GetProperty("traceId").GetGuid();
        if (this.seenTraces.Add(traceId))
        {
            this.state.Graph.Add(new GraphRow(
                string.Empty, "Trace", 0, $"── trace {traceId.ToString("N")[..8]}",
                item.GetProperty("origin").GetString() ?? string.Empty, string.Empty));
        }

        this.state.Graph.Add(new GraphRow(
            item.GetProperty("occurredAt").GetDateTimeOffset().ToLocalTime().ToString("HH:mm:ss"),
            item.GetProperty("status").GetString() ?? string.Empty,
            this.DepthOf(spanId),
            item.GetProperty("type").GetString() ?? string.Empty,
            item.GetProperty("actorId").GetString() ?? string.Empty,
            item.GetProperty("label").GetString() ?? string.Empty));
        this.GraphScroll.ScrollToEnd();
    }

    /// <summary>Depth in the span tree, walked from the parent links the runtime recorded.</summary>
    private int DepthOf(Guid spanId)
    {
        var depth = 0;
        var current = this.spanParents.GetValueOrDefault(spanId);
        while (current is not null && depth < 16 && this.spanParents.ContainsKey(current.Value))
        {
            depth++;
            current = this.spanParents.GetValueOrDefault(current.Value);
        }

        return this.spanParents.GetValueOrDefault(spanId) is null ? 0 : depth + 1;
    }
}
