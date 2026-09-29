using Dami.Contracts.Calendar;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Proactive.Calendar;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Calendar;

/// <summary>The calendar's secret address, read through the egress door into a local mirror of the next two weeks.</summary>
public sealed class CalendarCollectorServiceTests
{
    // 2026-09-29 15:00 CDT.
    private static readonly DateTimeOffset now = new(2026, 9, 29, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset midnightChicago = new(2026, 9, 29, 5, 0, 0, TimeSpan.Zero);

    private const string FEED = """
        BEGIN:VCALENDAR
        VERSION:2.0
        BEGIN:VEVENT
        DTSTART:20260930T143000Z
        DTEND:20260930T150000Z
        UID:dentist@google.com
        SUMMARY:Dentist
        END:VEVENT
        END:VCALENDAR
        """;

    private readonly IEgressClient egress = Substitute.For<IEgressClient>();
    private readonly ICalendarStore store = Substitute.For<ICalendarStore>();
    private readonly CalendarOptions options = new() { IcsUrl = "https://calendar.google.com/calendar/ical/x/private-y/basic.ics" };

    private CalendarCollectorService Subject() => new(
        this.store, this.egress, Options.Create(this.options), new FakeTimeProvider(now), NullLogger<CalendarCollectorService>.Instance);

    private static ProactiveContext Context() => new(Guid.NewGuid(), now, null);

    [Fact]
    public async Task Should_Mirror_Today_And_The_Next_Two_Weeks_From_The_Feed()
    {
        this.egress.SendAsync(Arg.Any<EgressRequest>(), Arg.Any<CancellationToken>()).Returns(new EgressResponse(200, FEED));

        var result = await this.Subject().RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal(ProactiveStatus.Completed, result.Status);
        await this.egress.Received(1).SendAsync(
            Arg.Is<EgressRequest>(request => request.Destination.Host == "calendar.google.com"), Arg.Any<CancellationToken>());
        await this.store.Received(1).ReplaceAsync(
            midnightChicago, midnightChicago.AddDays(15),
            Arg.Is<IReadOnlyList<CalendarEvent>>(events => events.Single().Summary == "Dentist"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Without_A_Configured_Address_Should_Do_Nothing_Quietly()
    {
        this.options.IcsUrl = string.Empty;

        var result = await this.Subject().RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal(ProactiveStatus.Completed, result.Status);
        await this.egress.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }

    [Theory]
    [InlineData(404, "not found")]
    [InlineData(200, "<html>sign in</html>")]
    public async Task A_Feed_That_Is_Not_A_Calendar_Should_Fail_The_Pass_And_Keep_The_Old_Mirror(int status, string body)
    {
        // A reset secret address answers with a sign-in page; emptying the mirror would make
        // "nothing today" look true. The reliability notice names a failed pass.
        this.egress.SendAsync(Arg.Any<EgressRequest>(), Arg.Any<CancellationToken>()).Returns(new EgressResponse(status, body));

        var result = await this.Subject().RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal(ProactiveStatus.Failed, result.Status);
        await this.store.DidNotReceiveWithAnyArgs().ReplaceAsync(default, default, default!, default);
    }
}
