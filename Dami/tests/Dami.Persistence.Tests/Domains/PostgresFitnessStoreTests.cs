using Dami.Persistence.Domains;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Domains;

/// <summary>The fitness domain read path (H9/G14) against the real 036 DDL.</summary>
[Collection(DatabaseCollection.NAME)]
public sealed class PostgresFitnessStoreTests
{
    private static readonly DateTimeOffset day1 = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset day2 = new(2026, 8, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresFitnessStoreTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public async Task SnapshotAsync_Should_Return_A_Cardio_Session_With_Its_Metrics()
    {
        await this.fixture.ResetAsync();
        var eventId = await this.SeedEventAsync(day1, "cardio");
        await this.SeedCardioAsync(eventId, "treadmill", 1800, 2.1m, 145, 152);

        var snapshot = await this.CreateStore().SnapshotAsync(CancellationToken.None);

        var session = Assert.Single(snapshot.Cardio);
        Assert.Equal(
            ("treadmill", 1800, 2.1m, 145, 152),
            (session.Modality, session.DurationSeconds, session.DistanceMi,
                session.HrAvg, session.HrMax));
    }

    [Fact]
    public async Task SnapshotAsync_Should_Join_A_Set_To_Its_Exercise_And_Session_Date()
    {
        await this.fixture.ResetAsync();
        var eventId = await this.SeedEventAsync(day2, "resistance");
        await this.SeedExerciseAsync(7, "Bench Press", "chest");
        await this.SeedSetAsync(eventId, exerciseId: 7, setNumber: 1, reps: 8, weightLbs: 135m);

        var snapshot = await this.CreateStore().SnapshotAsync(CancellationToken.None);

        var set = Assert.Single(snapshot.Sets);
        Assert.Equal(
            ("Bench Press", "chest", day2, (short)8, 135m),
            (set.Exercise, set.MuscleGroup, set.OccurredAt, set.Reps, set.WeightLbs));
    }

    [Fact]
    public async Task SnapshotAsync_Should_Name_A_Set_Whose_Exercise_Is_Missing()
    {
        // exercise_id is nullable in 036; a set must not vanish because its lookup did.
        await this.fixture.ResetAsync();
        var eventId = await this.SeedEventAsync(day1, "resistance");
        await this.SeedSetAsync(eventId, exerciseId: null, setNumber: 1, reps: 10, weightLbs: 50m);

        var snapshot = await this.CreateStore().SnapshotAsync(CancellationToken.None);

        Assert.Equal("unrecorded exercise", Assert.Single(snapshot.Sets).Exercise);
    }

    [Fact]
    public async Task SnapshotAsync_Should_Return_Weigh_Ins_Oldest_First()
    {
        await this.fixture.ResetAsync();
        var later = await this.SeedEventAsync(day2, "weight");
        var earlier = await this.SeedEventAsync(day1, "weight");
        await this.SeedWeightAsync(later, 189.2m);
        await this.SeedWeightAsync(earlier, 190.6m);

        var snapshot = await this.CreateStore().SnapshotAsync(CancellationToken.None);

        Assert.Equal(
            [190.6m, 189.2m],
            snapshot.WeighIns.Select(weighIn => weighIn.WeightLbs).ToList());
    }

    [Fact]
    public async Task SnapshotAsync_Should_Return_Empty_Lists_When_Nothing_Is_Recorded()
    {
        await this.fixture.ResetAsync();

        var snapshot = await this.CreateStore().SnapshotAsync(CancellationToken.None);

        Assert.Equal((0, 0, 0), (snapshot.Cardio.Count, snapshot.Sets.Count, snapshot.WeighIns.Count));
    }

    private PostgresFitnessStore CreateStore()
    {
        return new PostgresFitnessStore(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
    }

    private async Task<Guid> SeedEventAsync(DateTimeOffset occurredAt, string kind)
    {
        var id = Guid.NewGuid();
        await using var command = this.fixture.DataSource.CreateCommand(
            $"""
            insert into {DatabaseFixture.SCHEMA}.fitness_event
                (fitness_event_id, occurred_at, kind, precision, sensitivity, source)
            values (@id, @occurred_at, @kind, 'exact', 'normal', 'manual_entry');
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("kind", kind);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task SeedCardioAsync(
        Guid eventId, string modality, int durationSeconds, decimal distanceMi, int hrAvg, int hrMax)
    {
        await using var command = this.fixture.DataSource.CreateCommand(
            $"""
            insert into {DatabaseFixture.SCHEMA}.fitness_cardio
                (fitness_event_id, modality, duration_seconds, distance_mi, hr_avg, hr_max)
            values (@id, @modality, @duration_seconds, @distance_mi, @hr_avg, @hr_max);
            """);
        command.Parameters.AddWithValue("id", eventId);
        command.Parameters.AddWithValue("modality", modality);
        command.Parameters.AddWithValue("duration_seconds", durationSeconds);
        command.Parameters.AddWithValue("distance_mi", distanceMi);
        command.Parameters.AddWithValue("hr_avg", hrAvg);
        command.Parameters.AddWithValue("hr_max", hrMax);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedExerciseAsync(int exerciseId, string name, string muscleGroup)
    {
        await using var command = this.fixture.DataSource.CreateCommand(
            $"""
            insert into {DatabaseFixture.SCHEMA}.fitness_exercise
                (exercise_id, name, primary_muscle_group)
            values (@id, @name, @muscle_group);
            """);
        command.Parameters.AddWithValue("id", exerciseId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("muscle_group", muscleGroup);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedSetAsync(
        Guid eventId, int? exerciseId, short setNumber, short reps, decimal weightLbs)
    {
        await using var resistance = this.fixture.DataSource.CreateCommand(
            $"""
            insert into {DatabaseFixture.SCHEMA}.fitness_resistance (fitness_event_id)
            values (@id) on conflict do nothing;
            """);
        resistance.Parameters.AddWithValue("id", eventId);
        await resistance.ExecuteNonQueryAsync();

        await using var command = this.fixture.DataSource.CreateCommand(
            $"""
            insert into {DatabaseFixture.SCHEMA}.fitness_resistance_set
                (set_id, fitness_event_id, exercise_id, set_number, reps, weight_lbs)
            values (@set_id, @event_id, @exercise_id, @set_number, @reps, @weight_lbs);
            """);
        command.Parameters.AddWithValue("set_id", Guid.NewGuid());
        command.Parameters.AddWithValue("event_id", eventId);
        command.Parameters.AddWithValue("exercise_id", (object?)exerciseId ?? DBNull.Value);
        command.Parameters.AddWithValue("set_number", setNumber);
        command.Parameters.AddWithValue("reps", reps);
        command.Parameters.AddWithValue("weight_lbs", weightLbs);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedWeightAsync(Guid eventId, decimal weightLbs)
    {
        await using var command = this.fixture.DataSource.CreateCommand(
            $"""
            insert into {DatabaseFixture.SCHEMA}.fitness_weight (fitness_event_id, weight_lbs)
            values (@id, @weight_lbs);
            """);
        command.Parameters.AddWithValue("id", eventId);
        command.Parameters.AddWithValue("weight_lbs", weightLbs);
        await command.ExecuteNonQueryAsync();
    }
}
