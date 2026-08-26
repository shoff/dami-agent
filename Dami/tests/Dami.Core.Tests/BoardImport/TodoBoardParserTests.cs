using Dami.Core.BoardImport;
using Xunit;

namespace Dami.Core.Tests.BoardImport;

/// <summary>
/// Covers the grammar TODO.md actually uses, as measured against the live file:
/// 186 checklist entries over 15 lettered sections, five indent levels, four documented
/// markers plus one undocumented, and BLOCKED as a trailing annotation rather than a marker.
/// </summary>
public sealed class TodoBoardParserTests
{
    [Fact]
    public void Parse_Should_Take_Lettered_Sections_As_Epics()
    {
        var document = TodoBoardParser.Parse("""
            ## A · Host & infrastructure

            - [ ] A1 Provision the host
            """);

        var section = Assert.Single(document.Sections);
        Assert.Equal("A", section.Key);
        Assert.Equal("Host & infrastructure", section.Title);
    }

    [Fact]
    public void Parse_Should_Skip_Sections_That_Hold_No_Checklist_Items()
    {
        // Protocol, the end state, and Steve's queue carry prose and cross-references to
        // tasks that live in other sections. Importing them would duplicate those tasks.
        var document = TodoBoardParser.Parse("""
            ## Protocol

            - Task states: `[ ]` open

            ## A · Host

            - [ ] A1 Provision the host
            """);

        Assert.Equal("A", Assert.Single(document.Sections).Key);
    }

    [Theory]
    [InlineData("- [ ] A1 Open work", TodoState.Open)]
    [InlineData("- [x] A1 Finished work", TodoState.Done)]
    [InlineData("- [STEVE] A1 Needs his key", TodoState.NeedsSteve)]
    public void Parse_Should_Translate_The_Documented_Markers(string line, TodoState expected)
    {
        var entry = ParseOne(line);

        Assert.Equal(expected, entry.State);
        Assert.Equal("A1", entry.Id);
    }

    [Fact]
    public void Parse_Should_Keep_Owner_And_Claim_Date_From_An_In_Progress_Marker()
    {
        var entry = ParseOne("- [~ Claude 2026-08-24] A1 Import the blueprint");

        Assert.Equal(TodoState.InProgress, entry.State);
        Assert.Equal("Claude", entry.Owner);
        Assert.Equal(new DateOnly(2026, 8, 24), entry.ClaimedOn);
    }

    [Fact]
    public void Parse_Should_Read_Blocked_As_A_Trailing_Annotation_Not_A_Marker()
    {
        // The protocol documents `[BLOCKED: reason]` as a state, but every use in the file
        // is a trailing annotation on an open task: "- [ ] E3 UDP path `[BLOCKED: L-phase]`".
        var entry = ParseOne("- [ ] E3 UDP path for voice frames `[BLOCKED: L-phase]`");

        Assert.Equal("L-phase", entry.BlockedReason);
        Assert.Equal("E3", entry.Id);
        Assert.DoesNotContain("BLOCKED", entry.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_Should_Read_A_Trailing_Steve_Annotation_The_Way_It_Reads_The_Marker()
    {
        // "- [ ] B7 Kokoro classes … `[STEVE: whose memories are they]`" is not open work.
        // It waits on him exactly as a leading [STEVE] does, and the reason is worth keeping.
        var entry = ParseOne("- [ ] B7 Kokoro classes: import or leave? `[STEVE: whose memories are they]`");

        Assert.Equal(TodoState.NeedsSteve, entry.State);
        Assert.Equal("whose memories are they", entry.StateDetail);
        Assert.DoesNotContain("STEVE", entry.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_Should_Not_Let_An_Annotation_Override_A_Finished_Task()
    {
        var entry = ParseOne("- [x] A4 Backups done `[STEVE: destination + a GPG key]`");

        Assert.Equal(TodoState.Done, entry.State);
    }

    [Fact]
    public void Parse_Should_Nest_By_Indentation()
    {
        var document = TodoBoardParser.Parse("""
            ## G · Runtime

            - [x] G5 Parent
              - [x] G5a Child
                - [ ] G5a1 Grandchild
            - [ ] G6 Sibling
            """);

        var entries = document.Sections[0].Entries;
        Assert.Equal(["G5", "G6"], entries.Select(entry => entry.Id));
        var child = Assert.Single(entries[0].Children);
        Assert.Equal("G5a", child.Id);
        Assert.Equal("G5a1", Assert.Single(child.Children).Id);
    }

    [Fact]
    public void Parse_Should_Number_Siblings_In_File_Order()
    {
        var document = TodoBoardParser.Parse("""
            ## A · Host

            - [ ] A1 First
            - [ ] A2 Second
            - [ ] A3 Third
            """);

        Assert.Equal([0, 1, 2], document.Sections[0].Entries.Select(entry => entry.Position));
    }

    [Theory]
    [InlineData("- [STEVE] B6 **Close D-010** — review the pairs", "B6")]
    [InlineData("- [ ] **B9** ADR-0012 retention", "B9")]
    public void Parse_Should_Find_The_Id_Through_Bold_Markup(string line, string expected)
    {
        Assert.Equal(expected, ParseOne(line).Id);
    }

    [Fact]
    public void Parse_Should_Treat_A_Struck_Through_Id_As_A_Reference_Not_An_Identity()
    {
        // The real file has "- [x] G9 Frontier-informed turns" and, two lines later,
        // "- [STEVE] ~~G9~~ posture". The second is the question left over from the first,
        // not a second G9; reading the strikethrough as an id merges them and loses the
        // open work behind the done one.
        var document = TodoBoardParser.Parse("""
            ## G · Runtime

            - [x] G9 Frontier-informed turns
            - [STEVE] ~~G9~~ posture — should chat offer a brief unprompted?
            """);

        var posture = document.Sections[0].Entries[1];
        Assert.Null(posture.Id);
        Assert.Equal("posture", posture.Title[..7]);
        Assert.Contains(document.Anomalies, anomaly =>
            anomaly.Reason.Contains("struck through", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_Should_Report_An_Undocumented_Marker_Rather_Than_Guess_At_It()
    {
        // "[DEFERRED: correct as-is]" appears once and is not one of the four documented
        // states. Cancelled would be a guess: deferred work is not abandoned work.
        var document = TodoBoardParser.Parse("""
            ## D · Model layer

            - [DEFERRED: correct as-is] D5 Cheap-model routing — deliberately not built
            """);

        var entry = document.Sections[0].Entries[0];
        Assert.Equal(TodoState.Deferred, entry.State);
        Assert.Equal("correct as-is", entry.StateDetail);
        Assert.Contains(document.Anomalies, anomaly =>
            anomaly.Reason.Contains("DEFERRED", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_Should_Report_A_Marker_It_Cannot_Read_At_All()
    {
        var document = TodoBoardParser.Parse("""
            ## A · Host

            - [???] A1 Something odd
            """);

        Assert.Contains(document.Anomalies, anomaly => anomaly.Line.Contains("???", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_Should_Resolve_A_Dependency_That_Names_A_Task_In_The_File()
    {
        var document = TodoBoardParser.Parse("""
            ## H · Proactive

            - [ ] H9 Domain collectors — needs K1 first

            ## K · Domains

            - [ ] K1 Domain inventory
            """);

        Assert.Equal(["K1"], document.Sections[0].Entries[0].DependsOnIds);
    }

    [Fact]
    public void Parse_Should_Report_Dependency_Prose_That_Names_No_Task()
    {
        // "decide after voice proves itself" is a real dependency and an unresolvable one.
        // Inventing an edge for it would put a false prerequisite in the graph.
        var document = TodoBoardParser.Parse("""
            ## L · Voice

            - [ ] L6 Avatar: decide after voice proves itself
            """);

        Assert.Empty(document.Sections[0].Entries[0].DependsOnIds);
        Assert.Contains(document.Anomalies, anomaly =>
            anomaly.Reason.Contains("dependency", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_Should_Not_Invent_A_Dependency_From_An_Unrelated_Needs()
    {
        var document = TodoBoardParser.Parse("""
            ## B · Data

            - [STEVE] B7 Kokoro classes — needs Steve's decision
            """);

        Assert.Empty(document.Sections[0].Entries[0].DependsOnIds);
    }

    [Fact]
    public void Parse_Should_Keep_Acceptance_References()
    {
        var entry = ParseOne("- [x] G4 Sessions: multi-turn conversation — acceptance item 1");

        Assert.Equal(["acceptance item 1"], entry.AcceptanceItems);
    }

    [Fact]
    public void Parse_Should_Keep_The_Whole_Line_So_Nothing_Is_Lost()
    {
        const string line = "- [x] G2 Context assembly (`ContextBuilder`): hard token budget";

        Assert.Equal(line, ParseOne(line).RawText);
    }

    [Fact]
    public void Parse_Should_Reject_A_Null_Document()
    {
        Assert.Throws<ArgumentNullException>(() => TodoBoardParser.Parse(null!));
    }

    private static TodoEntry ParseOne(string line)
    {
        var document = TodoBoardParser.Parse($"## A · Host\n\n{line}\n");
        return document.Sections[0].Entries[0];
    }

    [Fact]
    public void Parse_Should_Read_A_Dash_Marker_As_Cancelled()
    {
        var document = TodoBoardParser.Parse("## Q · Quiet\n\n- [-] Q1 Dropped on the board\n");

        var entry = Assert.Single(Assert.Single(document.Sections).Entries);
        Assert.Equal((TodoState.Cancelled, "Q1"), (entry.State, entry.Id));
        Assert.Empty(document.Anomalies);
    }
}
