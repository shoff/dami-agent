using System.Text.Json;
using Dami.Contracts.Domains;
using Dami.Core.Frontier;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Frontier;

/// <summary>"4x12 140 lbs RPE 7" plus a photo becomes rows in the exercise log.</summary>
public sealed class FitnessToolsTests
{
    private static readonly DateTimeOffset now = new(2026, 9, 5, 20, 0, 0, TimeSpan.Zero);

    private readonly IFitnessStore store = Substitute.For<IFitnessStore>();

    private FitnessTools Subject() => new(this.store, new FakeTimeProvider(now), NullLogger<FitnessTools>.Instance);

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task Log_Sets_Should_Expand_Sets_By_Reps_Into_One_Resistance_Entry()
    {
        this.store.RecordResistanceAsync(Arg.Any<FitnessResistanceEntry>(), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());

        var result = await this.Subject().LogSetsAsync(
            Args("""{"exercise":"biceps curl (Hammer Strength)","sets":4,"reps":12,"weightLbs":140,"rpe":7,"equipment":"Machine"}"""),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.StartsWith("Logged 4x12 biceps curl (Hammer Strength) at 140 lb, RPE 7.", result.Text, StringComparison.Ordinal);
        await this.store.Received(1).RecordResistanceAsync(
            Arg.Is<FitnessResistanceEntry>(entry =>
                entry.Exercise == "biceps curl (Hammer Strength)"
                && entry.Sets.Count == 4
                && entry.Sets.All(set => set.Reps == 12 && set.WeightLbs == 140 && set.Rpe == 7)
                && entry.Equipment == "machine"
                && entry.Source == "claude_chat"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Log_Sets_Should_Say_What_The_Log_Noticed_About_That_Exercise()
    {
        // A record announced the moment it is logged, with the numbers.
        var earlier = new FitnessSet(Guid.NewGuid(), Guid.NewGuid(), now.AddDays(-20), "biceps curl", "biceps", 1, 12, 120m, 7, false);
        var today = new FitnessSet(Guid.NewGuid(), Guid.NewGuid(), now, "biceps curl", "biceps", 1, 12, 140m, 7, false);
        this.store.RecordResistanceAsync(Arg.Any<FitnessResistanceEntry>(), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());
        this.store.SnapshotAsync(Arg.Any<CancellationToken>()).Returns(new FitnessSnapshot([], [earlier, today], []));

        var result = await this.Subject().LogSetsAsync(Args("""{"exercise":"biceps curl","sets":4,"reps":12,"weightLbs":140,"rpe":7}"""), CancellationToken.None);

        Assert.Contains("Noticed", result.Text, StringComparison.Ordinal);
        Assert.Contains("New best on biceps curl: 140 lb × 12", result.Text, StringComparison.Ordinal);
        Assert.Contains("try 145 lb next time", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Log_Sets_Should_Land_On_The_Logs_Own_Spelling_Of_The_Exercise()
    {
        // 2026-09-06: "biceps curl (Hammer Strength)" was minted beside seven curl entries and
        // the history never lined up. Same name, different case or spacing, is the same row.
        var earlier = new FitnessSet(Guid.NewGuid(), Guid.NewGuid(), now.AddDays(-7), "Biceps Curl Machine", "biceps", 1, 12, 100m, 7, false);
        this.store.SnapshotAsync(Arg.Any<CancellationToken>()).Returns(new FitnessSnapshot([], [earlier], []));
        this.store.RecordResistanceAsync(Arg.Any<FitnessResistanceEntry>(), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());

        await this.Subject().LogSetsAsync(Args("""{"exercise":"biceps  curl machine","sets":3,"reps":12,"weightLbs":105}"""), CancellationToken.None);

        await this.store.Received(1).RecordResistanceAsync(
            Arg.Is<FitnessResistanceEntry>(entry => entry.Exercise == "Biceps Curl Machine"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Log_Sets_Should_Hand_Back_What_The_Log_Held_Before_Today()
    {
        // "Try 115 lb next time" with no history in hand is a guess. The result carries the
        // last sessions and the best, so the comparison is numbers.
        var justLogged = Guid.NewGuid();
        var lastWeek = new FitnessSet(Guid.NewGuid(), Guid.NewGuid(), now.AddDays(-7), "leg press", null, 1, 10, 300m, 7, false);
        var today = new FitnessSet(Guid.NewGuid(), justLogged, now, "leg press", null, 1, 10, 320m, 7, false);
        this.store.SnapshotAsync(Arg.Any<CancellationToken>()).Returns(new FitnessSnapshot([], [lastWeek, today], []));
        this.store.RecordResistanceAsync(Arg.Any<FitnessResistanceEntry>(), Arg.Any<CancellationToken>()).Returns(justLogged);

        var result = await this.Subject().LogSetsAsync(Args("""{"exercise":"leg press","sets":1,"reps":10,"weightLbs":320}"""), CancellationToken.None);

        Assert.Contains("Before today on leg press (1 session(s)): 2026-08-29: 1x10 at 300 lb RPE 7.", result.Text, StringComparison.Ordinal);
        Assert.Contains("Best before today: 300 lb × 10 on 2026-08-29.", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sets_Tool_Should_Name_The_Exercises_The_Log_Already_Uses()
    {
        var older = new FitnessSet(Guid.NewGuid(), Guid.NewGuid(), now.AddDays(-9), "hammer curl", null, 1, 12, 30m, null, false);
        var newer = new FitnessSet(Guid.NewGuid(), Guid.NewGuid(), now.AddDays(-2), "biceps curl machine", null, 1, 12, 100m, null, false);
        this.store.SnapshotAsync(Arg.Any<CancellationToken>()).Returns(new FitnessSnapshot([], [older, newer], []));

        var tool = await this.Subject().SetsToolAsync(CancellationToken.None);

        Assert.Equal("log_sets", tool.Name);
        Assert.Contains("biceps curl machine; hammer curl", tool.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("one short line", tool.Description, StringComparison.Ordinal);
        Assert.Contains("ask how he is feeling", tool.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sets_Tool_Should_Still_Exist_When_The_Log_Cannot_Be_Read()
    {
        this.store.SnapshotAsync(Arg.Any<CancellationToken>()).Returns<Task<FitnessSnapshot>>(_ => throw new InvalidOperationException("db down"));

        var tool = await this.Subject().SetsToolAsync(CancellationToken.None);

        Assert.Equal("log_sets", tool.Name);
        Assert.DoesNotContain("already in his log", tool.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Log_Sets_Should_Refuse_Nonsense_Before_Writing()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => this.Subject().LogSetsAsync(Args("""{"exercise":"x","sets":0,"reps":12}"""), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(
            () => this.Subject().LogSetsAsync(Args("""{"sets":3,"reps":12}"""), CancellationToken.None));

        await this.store.DidNotReceive().RecordResistanceAsync(Arg.Any<FitnessResistanceEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Log_Cardio_Should_Convert_Minutes_And_Keep_The_Display_Numbers()
    {
        this.store.RecordCardioAsync(Arg.Any<FitnessCardioEntry>(), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());

        var result = await this.Subject().LogCardioAsync(
            Args("""{"modality":"Treadmill","minutes":30.5,"distanceMi":2.1,"calories":310,"inclinePct":3,"hrAvg":138}"""),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Logged treadmill for 30.5 min, 2.1 mi.", result.Text);
        await this.store.Received(1).RecordCardioAsync(
            Arg.Is<FitnessCardioEntry>(entry =>
                entry.Modality == "treadmill" && entry.DurationSeconds == 1830 && entry.Calories == 310
                && entry.InclinePct == 3 && entry.HrAvg == 138),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Log_Cardio_Should_Name_The_Allowed_Modalities_When_Given_An_Unknown_One()
    {
        var result = await this.Subject().LogCardioAsync(Args("""{"modality":"stairmaster"}"""), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("elliptical", result.Text, StringComparison.Ordinal);
        await this.store.DidNotReceive().RecordCardioAsync(Arg.Any<FitnessCardioEntry>(), Arg.Any<CancellationToken>());
    }
}
