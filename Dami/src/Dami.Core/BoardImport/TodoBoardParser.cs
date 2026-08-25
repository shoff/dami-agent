using System.Globalization;
using System.Text.RegularExpressions;

namespace Dami.Core.BoardImport;

/// <summary>Reads TODO.md into a task tree, reporting what it cannot read.</summary>
/// <remarks>
/// The grammar here is the one the file actually uses, measured rather than assumed:
/// 186 checklist entries across 15 lettered sections, five indent levels at two spaces
/// each, the four markers the protocol documents plus <c>[DEFERRED: …]</c> which it does
/// not, and BLOCKED as a trailing annotation on an open task rather than a marker of its
/// own. Prerequisites have no syntax at all — they are prose — so a dependency is only
/// recorded when it names an id that exists in the file, and reported otherwise.
/// </remarks>
public static partial class TodoBoardParser
{
    private const int SPACES_PER_LEVEL = 2;

    /// <summary>Reads a TODO.md document.</summary>
    /// <param name="markdown">The file's text.</param>
    /// <returns>The sections it defines and the anomalies found reading them.</returns>
    public static TodoDocument Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var anomalies = new List<TodoAnomaly>();
        var sections = ReadSections(markdown, anomalies);
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in sections)
        {
            Collect(section.Roots, known);
        }

        var resolved = sections
            .Where(section => section.Roots.Count > 0)
            .Select((section, index) => new TodoSection(
                section.Key,
                section.Title,
                index,
                Freeze(section.Roots, known, anomalies)))
            .ToList();

        return new TodoDocument(resolved, anomalies);
    }

    /// <summary>Walks the file once, attaching each entry to its parent by indentation.</summary>
    private static List<Section> ReadSections(string markdown, List<TodoAnomaly> anomalies)
    {
        var sections = new List<Section>();
        var stack = new List<Node>();
        var lines = markdown.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            var heading = HeadingPattern().Match(line);
            if (heading.Success)
            {
                sections.Add(new Section(heading.Groups[1].Value, heading.Groups[2].Value.Trim()));
                stack.Clear();
                continue;
            }

            var item = ItemPattern().Match(line);
            if (!item.Success || sections.Count == 0)
            {
                continue;
            }

            Attach(sections[^1], stack, ReadEntry(item, line, index + 1, anomalies));
        }

        return sections;
    }

    private static void Attach(Section section, List<Node> stack, Node node)
    {
        while (stack.Count > node.Depth)
        {
            stack.RemoveAt(stack.Count - 1);
        }

        if (stack.Count == 0)
        {
            section.Roots.Add(node);
        }
        else
        {
            stack[^1].Children.Add(node);
        }

        stack.Add(node);
    }

    private static Node ReadEntry(Match item, string line, int lineNumber, List<TodoAnomaly> anomalies)
    {
        var depth = item.Groups[1].Value.Length / SPACES_PER_LEVEL;
        var marker = ReadMarker(item.Groups[2].Value, line, lineNumber, anomalies);
        var text = item.Groups[3].Value.Trim();

        var blocked = BlockedPattern().Match(text);
        if (blocked.Success)
        {
            text = text.Remove(blocked.Index, blocked.Length).Trim();
        }

        var id = ReadId(text, line, lineNumber, anomalies);
        return new Node
        {
            Depth = depth,
            LineNumber = lineNumber,
            RawText = line,
            Marker = marker,
            Id = id.Value,
            Title = text[id.Consumed..].Trim(' ', '—', '-', ':'),
            BlockedReason = blocked.Success ? blocked.Groups[1].Value.Trim() : null,
            Acceptance = [.. AcceptancePattern().Matches(text).Select(match => match.Value)],
        };
    }

    /// <summary>Translates a leading marker, reporting anything the protocol does not define.</summary>
    private static Marker ReadMarker(string marker, string line, int lineNumber, List<TodoAnomaly> anomalies)
    {
        var documented = Documented(marker);
        if (documented is not null)
        {
            return documented;
        }

        var claim = ClaimPattern().Match(marker);
        if (claim.Success)
        {
            return new Marker(
                TodoState.InProgress,
                claim.Groups[1].Value,
                DateOnly.ParseExact(claim.Groups[2].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                null);
        }

        var deferred = DeferredPattern().Match(marker);
        if (deferred.Success)
        {
            anomalies.Add(new TodoAnomaly(
                lineNumber,
                line,
                "DEFERRED is not one of the protocol's states; imported without a status so it is not "
                    + "silently recorded as cancelled."));
            return new Marker(TodoState.Deferred, null, null, deferred.Groups[1].Value.Trim());
        }

        anomalies.Add(new TodoAnomaly(lineNumber, line, $"Unreadable task marker '[{marker}]'."));
        return new Marker(TodoState.Open, null, null, null);
    }

    /// <summary>Reads the leading task id, if the line owns one.</summary>
    /// <remarks>
    /// A struck-through id is a reference, not an identity: "- [STEVE] ~~G9~~ posture" sits
    /// beside a live "- [x] G9" and is the question left over from it, not a second G9.
    /// Reading it as this entry's id collapses two tasks into one and loses the open work,
    /// so it is reported and the entry is left to be identified some other way.
    /// </remarks>
    private static (string? Value, int Consumed) ReadId(
        string text,
        string line,
        int lineNumber,
        List<TodoAnomaly> anomalies)
    {
        var match = IdPattern().Match(text);
        if (!match.Success)
        {
            return (null, 0);
        }

        if (match.Groups[1].Value.Length > 0)
        {
            anomalies.Add(new TodoAnomaly(
                lineNumber,
                line,
                $"The id '{match.Groups[2].Value}' is struck through, so it reads as a reference to that "
                    + "task rather than this entry's own id. Imported under a derived id instead."));
            return (null, match.Length);
        }

        return (match.Groups[2].Value, match.Length);
    }

    /// <summary>The three markers that carry no payload.</summary>
    private static Marker? Documented(string marker)
    {
        return marker switch
        {
            " " or "" => new Marker(TodoState.Open, null, null, null),
            "x" => new Marker(TodoState.Done, null, null, null),
            "STEVE" => new Marker(TodoState.NeedsSteve, null, null, null),
            _ => null,
        };
    }

    private static void Collect(List<Node> nodes, HashSet<string> known)
    {
        foreach (var node in nodes)
        {
            if (node.Id is not null)
            {
                known.Add(node.Id);
            }

            Collect(node.Children, known);
        }
    }

    /// <summary>Turns the mutable tree into records, resolving dependencies as it goes.</summary>
    private static IReadOnlyList<TodoEntry> Freeze(
        List<Node> nodes,
        HashSet<string> known,
        List<TodoAnomaly> anomalies)
    {
        var entries = new List<TodoEntry>(nodes.Count);
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            entries.Add(new TodoEntry(
                node.Id,
                node.Title,
                node.RawText,
                node.Marker.State,
                node.Marker.Owner,
                node.Marker.ClaimedOn,
                node.BlockedReason,
                node.Marker.Detail,
                node.LineNumber,
                index,
                node.Acceptance,
                Dependencies(node, known, anomalies),
                Freeze(node.Children, known, anomalies)));
        }

        return entries;
    }

    /// <summary>
    /// Prerequisites written as prose. An edge is recorded only when the phrase names an id
    /// this file defines; anything else is reported, because a guessed edge is a false
    /// prerequisite in the graph and nothing downstream could tell it from a real one.
    /// </summary>
    private static IReadOnlyList<string> Dependencies(
        Node node,
        HashSet<string> known,
        List<TodoAnomaly> anomalies)
    {
        var found = new List<string>();
        foreach (Match match in DependencyPattern().Matches(node.RawText))
        {
            var candidate = match.Groups[2].Value;
            if (known.Contains(candidate) && candidate != node.Id)
            {
                found.Add(candidate);
                continue;
            }

            anomalies.Add(new TodoAnomaly(
                node.LineNumber,
                node.RawText,
                $"Unresolved dependency: '{match.Groups[1].Value} {candidate}' names no task in this file."));
        }

        return found;
    }

    [GeneratedRegex(@"^##\s+([A-Z])\s+·\s+(.+)$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^([ ]*)- \[([^\]]*)\]\s+(.*)$")]
    private static partial Regex ItemPattern();

    [GeneratedRegex(@"^~\s+(\S+)\s+(\d{4}-\d{2}-\d{2})$")]
    private static partial Regex ClaimPattern();

    [GeneratedRegex(@"^DEFERRED:\s*(.*)$")]
    private static partial Regex DeferredPattern();

    [GeneratedRegex(@"`?\[BLOCKED:\s*([^\]]*)\]`?")]
    private static partial Regex BlockedPattern();

    [GeneratedRegex(@"^(?:\*\*|(~~))?([A-Z]\d+[a-z0-9]*)\b(?:\*\*|~~)?")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"acceptance item \d+", RegexOptions.IgnoreCase)]
    private static partial Regex AcceptancePattern();

    [GeneratedRegex(
        @"\b(depends on|blocked by|prerequisites?|waits on|requires|needs|after)\s+(\S+?)[\s,.;:]",
        RegexOptions.IgnoreCase)]
    private static partial Regex DependencyPattern();

    private sealed record Marker(TodoState State, string? Owner, DateOnly? ClaimedOn, string? Detail);

    private sealed class Section(string key, string title)
    {
        public string Key { get; } = key;

        public string Title { get; } = title;

        public List<Node> Roots { get; } = [];
    }

    private sealed class Node
    {
        public required int Depth { get; init; }

        public required int LineNumber { get; init; }

        public required string RawText { get; init; }

        public required Marker Marker { get; init; }

        public required string? Id { get; init; }

        public required string Title { get; init; }

        public required string? BlockedReason { get; init; }

        public required IReadOnlyList<string> Acceptance { get; init; }

        public List<Node> Children { get; } = [];
    }
}
