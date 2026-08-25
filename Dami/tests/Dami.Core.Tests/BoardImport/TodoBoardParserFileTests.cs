using Dami.Core.BoardImport;
using Xunit;
using Xunit.Abstractions;

namespace Dami.Core.Tests.BoardImport;

/// <summary>
/// Reads the repository's own TODO.md. The unit tests fix the grammar against examples;
/// this one fixes it against the file the importer will actually be run on, so a change to
/// the board's conventions surfaces here rather than in a half-imported board.
/// </summary>
public sealed class TodoBoardParserFileTests
{
    private readonly ITestOutputHelper output;

    /// <summary>Creates the fixture.</summary>
    public TodoBoardParserFileTests(ITestOutputHelper output)
    {
        ArgumentNullException.ThrowIfNull(output);
        this.output = output;
    }

    [Fact]
    public void Parse_Should_Read_The_Repositorys_Board()
    {
        var document = TodoBoardParser.Parse(File.ReadAllText(FindBoard()));
        var entries = Flatten(document).ToList();

        this.output.WriteLine($"sections: {document.Sections.Count}");
        this.output.WriteLine($"entries:  {entries.Count}");
        this.output.WriteLine($"depth:    {entries.Max(entry => Depth(document, entry))}");
        foreach (var group in entries.GroupBy(entry => entry.State).OrderBy(group => group.Key))
        {
            this.output.WriteLine($"  {group.Key,-12} {group.Count()}");
        }

        this.output.WriteLine($"blocked:     {entries.Count(entry => entry.BlockedReason is not null)}");
        this.output.WriteLine($"acceptance:  {entries.Count(entry => entry.AcceptanceItems.Count > 0)}");
        this.output.WriteLine($"prereq edges:{entries.Sum(entry => entry.DependsOnIds.Count)}");
        this.output.WriteLine($"anomalies:   {document.Anomalies.Count}");
        foreach (var anomaly in document.Anomalies)
        {
            this.output.WriteLine($"  line {anomaly.LineNumber}: {anomaly.Reason}");
        }

        Assert.NotEmpty(document.Sections);
        Assert.All(document.Sections, section => Assert.NotEmpty(section.Entries));
    }

    [Fact]
    public void Parse_Should_Give_Every_Entry_An_Id_Or_Report_It()
    {
        var document = TodoBoardParser.Parse(File.ReadAllText(FindBoard()));

        // A task without an id cannot get a deterministic identity from the file, so the
        // importer has to derive one. That is acceptable only when it is reported: silently
        // minting ids is how an import stops being idempotent without anyone noticing.
        var unnamed = Flatten(document).Where(entry => entry.Id is null).ToList();
        foreach (var entry in unnamed)
        {
            Assert.Contains(document.Anomalies, anomaly => anomaly.LineNumber == entry.LineNumber);
        }

        this.output.WriteLine($"entries needing a derived id: {unnamed.Count}");
    }

    [Fact]
    public void Parse_Should_Not_Produce_Duplicate_Ids()
    {
        var document = TodoBoardParser.Parse(File.ReadAllText(FindBoard()));

        // Ids are the import's deterministic key. Two tasks sharing one would collapse into
        // a single board task and silently lose work.
        var duplicates = Flatten(document)
            .Where(entry => entry.Id is not null)
            .GroupBy(entry => entry.Id!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.True(duplicates.Count == 0, $"duplicate ids: {string.Join(", ", duplicates)}");
    }

    private static int Depth(TodoDocument document, TodoEntry entry)
    {
        foreach (var section in document.Sections)
        {
            var depth = Depth(section.Entries, entry, 1);
            if (depth > 0)
            {
                return depth;
            }
        }

        return 0;
    }

    private static int Depth(IReadOnlyList<TodoEntry> entries, TodoEntry target, int depth)
    {
        foreach (var entry in entries)
        {
            if (ReferenceEquals(entry, target))
            {
                return depth;
            }

            var found = Depth(entry.Children, target, depth + 1);
            if (found > 0)
            {
                return found;
            }
        }

        return 0;
    }

    private static IEnumerable<TodoEntry> Flatten(TodoDocument document)
    {
        return document.Sections.SelectMany(section => Flatten(section.Entries));
    }

    private static IEnumerable<TodoEntry> Flatten(IReadOnlyList<TodoEntry> entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;
            foreach (var child in Flatten(entry.Children))
            {
                yield return child;
            }
        }
    }

    /// <summary>Walks up from the test binary to the checkout, the way the DDL tests do.</summary>
    private static string FindBoard()
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            var candidate = Path.Combine(directory, "TODO.md");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
        }

        throw new InvalidOperationException(
            $"Could not locate TODO.md above {AppContext.BaseDirectory}. This test reads the checkout.");
    }
}
