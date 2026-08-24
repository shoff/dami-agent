using System.Collections.ObjectModel;

namespace Dami.Gui;

/// <summary>One line in the conversation.</summary>
public sealed class Message
{
    /// <summary>Creates a message.</summary>
    public Message(string who, string body)
    {
        this.Who = who;
        this.Body = body;
    }

    /// <summary>Who said it — "you" or "dami".</summary>
    public string Who { get; }

    /// <summary>What was said. Grows while a turn streams.</summary>
    public string Body { get; set; }

    /// <summary>Accounting shown under Dami's replies once the turn reports it.</summary>
    public string Meta { get; set; } = string.Empty;

    /// <summary>True when this is Steve's own line, for styling.</summary>
    public bool IsYou => this.Who == "you";
}

/// <summary>One event in the live execution graph, already positioned in its span tree.</summary>
public sealed class GraphRow
{
    /// <summary>Creates a row.</summary>
    public GraphRow(string time, string status, int depth, string type, string actor, string label)
    {
        this.Time = time;
        this.Status = status;
        this.Depth = depth;
        this.Type = type;
        this.Actor = actor;
        this.Label = label;
    }

    /// <summary>When it happened.</summary>
    public string Time { get; }

    /// <summary>Running, Succeeded, Failed — drives the colour.</summary>
    public string Status { get; }

    /// <summary>Depth in the span tree; a child sits under its parent.</summary>
    public int Depth { get; }

    /// <summary>Indentation derived from <see cref="Depth"/>.</summary>
    public Avalonia.Thickness Indent => new(this.Depth * 16, 0, 0, 0);

    /// <summary>The event type.</summary>
    public string Type { get; }

    /// <summary>Which component acted.</summary>
    public string Actor { get; }

    /// <summary>The human-readable label. Never invented — it is what was persisted.</summary>
    public string Label { get; }
}

/// <summary>One item awaiting Steve's decision, or one thing Dami believes.</summary>
public sealed class SidebarItem
{
    /// <summary>Creates an item.</summary>
    public SidebarItem(string id, string headline, string detail)
    {
        this.Id = id;
        this.Headline = headline;
        this.Detail = detail;
    }

    /// <summary>Short id, for acting on it.</summary>
    public string Id { get; }

    /// <summary>The line that matters.</summary>
    public string Headline { get; }

    /// <summary>Provenance or context.</summary>
    public string Detail { get; }
}

/// <summary>Everything the window binds to.</summary>
public sealed class WindowState
{
    /// <summary>The conversation, oldest first.</summary>
    public ObservableCollection<Message> Messages { get; } = [];

    /// <summary>The live execution graph.</summary>
    public ObservableCollection<GraphRow> Graph { get; } = [];

    /// <summary>Pending surfacings and approvals.</summary>
    public ObservableCollection<SidebarItem> Attention { get; } = [];

    /// <summary>The active belief ledger.</summary>
    public ObservableCollection<SidebarItem> Beliefs { get; } = [];
}
