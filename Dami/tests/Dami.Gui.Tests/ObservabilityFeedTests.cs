using System.Text.Json;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class ObservabilityFeedTests
{
    [Fact]
    public void Build_Should_Make_Current_Work_And_Noteworthy_Outcomes_Visible()
    {
        using var events = JsonDocument.Parse("""
            [
              {"sequence":41,"traceId":"66666666-6666-6666-6666-666666666666","origin":"UserTurn","actorId":"frontier-codex","type":"EgressRequested","status":"Running","occurredAt":"2026-09-09T22:03:44-05:00","label":"frontier turn"},
              {"sequence":42,"traceId":"11111111-1111-1111-1111-111111111111","origin":"UserTurn","actorId":"research","type":"EgressCompleted","status":"Succeeded","occurredAt":"2026-09-09T22:03:54-05:00","label":"api.weather.gov answered 200"},
              {"sequence":43,"traceId":"11111111-1111-1111-1111-111111111111","origin":"UserTurn","actorId":"research","type":"EgressRefused","status":"Failed","occurredAt":"2026-09-09T22:03:55-05:00","label":"unsupported application/pdf"},
              {"sequence":44,"traceId":"22222222-2222-2222-2222-222222222222","origin":"ScheduledService","actorId":"weather","type":"WorkerStarted","status":"Running","occurredAt":"2026-09-09T22:04:00-05:00","label":"weather started"},
              {"sequence":45,"traceId":"55555555-5555-5555-5555-555555555555","origin":"ScheduledService","actorId":"weather","type":"TraceCompleted","status":"Succeeded","occurredAt":"2026-09-09T22:04:02-05:00","label":"weather complete"}
            ]
            """);

        var snapshot = ObservabilityFeed.Build(events.RootElement);

        Assert.Equal([45L, 44L, 43L, 42L, 41L], snapshot.Events.Select(item => item.Sequence));
        Assert.Equal(["5", "2", "1", "1"], snapshot.Tiles.Select(item => item.Value));
        Assert.Equal("22:04:00", snapshot.Events[1].Time);
        Assert.True(snapshot.Events[2].IsAlert);
        Assert.Equal("11111111", snapshot.Events[2].Trace);
    }

    [Fact]
    public void Build_Should_Not_Count_A_Completed_Span_As_Running()
    {
        using var events = JsonDocument.Parse("""
            [
              {"sequence":61,"traceId":"11111111-1111-1111-1111-111111111111","spanId":"33333333-3333-3333-3333-333333333333","origin":"UserTurn","actorId":"research","type":"EgressRequested","status":"Running","occurredAt":"2026-09-09T22:03:44-05:00","label":"fetching API"},
              {"sequence":62,"traceId":"11111111-1111-1111-1111-111111111111","spanId":"33333333-3333-3333-3333-333333333333","origin":"UserTurn","actorId":"research","type":"EgressCompleted","status":"Succeeded","occurredAt":"2026-09-09T22:03:45-05:00","label":"API answered 200"}
            ]
            """);

        var snapshot = ObservabilityFeed.Build(events.RootElement);

        Assert.Equal("0", snapshot.Tiles.Single(item => item.Label == "RUNNING").Value);
    }

    [Fact]
    public void Build_Should_Not_Count_A_Completed_Trace_When_Its_Lifecycle_Spans_Differ()
    {
        using var events = JsonDocument.Parse("""
            [
              {"sequence":71,"traceId":"11111111-1111-1111-1111-111111111111","spanId":"33333333-3333-3333-3333-333333333333","origin":"UserTurn","actorId":"runtime","type":"TraceStarted","status":"Running","occurredAt":"2026-09-09T22:03:44-05:00","label":"turn started"},
              {"sequence":72,"traceId":"11111111-1111-1111-1111-111111111111","spanId":"44444444-4444-4444-4444-444444444444","origin":"UserTurn","actorId":"runtime","type":"TraceCompleted","status":"Succeeded","occurredAt":"2026-09-09T22:03:45-05:00","label":"turn complete"}
            ]
            """);

        var snapshot = ObservabilityFeed.Build(events.RootElement);

        Assert.Equal("0", snapshot.Tiles.Single(item => item.Label == "RUNNING").Value);
    }

    [Fact]
    public void Build_Should_Not_Count_Completed_Egress_When_Its_Event_Spans_Differ()
    {
        using var events = JsonDocument.Parse("""
            [
              {"sequence":81,"traceId":"11111111-1111-1111-1111-111111111111","spanId":"33333333-3333-3333-3333-333333333333","origin":"UserTurn","actorId":"research","type":"EgressRequested","status":"Running","occurredAt":"2026-09-09T22:03:44-05:00","label":"fetching API"},
              {"sequence":82,"traceId":"11111111-1111-1111-1111-111111111111","spanId":"44444444-4444-4444-4444-444444444444","origin":"UserTurn","actorId":"research","type":"EgressCompleted","status":"Succeeded","occurredAt":"2026-09-09T22:03:45-05:00","label":"API answered 200"}
            ]
            """);

        var snapshot = ObservabilityFeed.Build(events.RootElement);

        Assert.Equal("0", snapshot.Tiles.Single(item => item.Label == "RUNNING").Value);
    }
}
