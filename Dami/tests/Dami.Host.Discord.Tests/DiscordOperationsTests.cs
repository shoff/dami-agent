using Dami.Contracts.Proactive;
using Xunit;

namespace Dami.Host.Discord.Tests;

public sealed class DiscordOperationsTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 30, 17, 0, 0, TimeSpan.Zero);

    private static ProactiveServiceHistory Service(
        string name, int alerts, double hoursAgo, ProactiveCadence? cadence = ProactiveCadence.Nightly) =>
        new(
            name,
            5,
            now.AddHours(-hoursAgo),
            ProactiveStatus.Completed,
            cadence,
            10,
            2,
            alerts,
            []);

    [Theory]
    [InlineData("status")]
    [InlineData("  STATUS  ")]
    [InlineData("!status")]
    [InlineData("/services")]
    [InlineData("workers")]
    public void Classify_Should_Recognise_A_Status_Question(string message)
    {
        Assert.Equal(DiscordOperations.Intent.Status, DiscordOperations.Classify(message));
    }

    [Theory]
    [InlineData("help")]
    [InlineData("?")]
    public void Classify_Should_Recognise_Help(string message)
    {
        Assert.Equal(DiscordOperations.Intent.Help, DiscordOperations.Classify(message));
    }

    [Theory]
    [InlineData("what is my blood pressure")]
    [InlineData("how did the status of my health change")]
    [InlineData("where was I on Tuesday")]
    [InlineData("")]
    public void Classify_Should_Send_Anything_Else_Down_The_General_Path(string message)
    {
        // Deliberately narrow. A fuzzy match that read "status" out of a personal question
        // would route a memory-bearing query into the path that has no gate on it.
        Assert.Equal(DiscordOperations.Intent.None, DiscordOperations.Classify(message));
    }

    [Fact]
    public void Status_Should_Say_So_When_Nothing_Has_Run()
    {
        Assert.Contains(
            "No proactive service",
            DiscordOperations.Status([], now),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Status_Should_Name_Each_Service_And_Its_Age()
    {
        var report = DiscordOperations.Status(
            [Service("curator", alerts: 0, hoursAgo: 2)], now);

        Assert.Contains("curator", report, StringComparison.Ordinal);
        Assert.Contains("2 h ago", report, StringComparison.Ordinal);
        Assert.Contains("5 run(s)", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Status_Should_Flag_A_Service_With_Alerts()
    {
        // interest-scout completed six passes while a server answered 429 at it. A status
        // report that called that healthy would repeat the failure it exists to surface.
        var report = DiscordOperations.Status(
            [Service("interest-scout", alerts: 6, hoursAgo: 1)], now);

        Assert.Contains("⚠", report, StringComparison.Ordinal);
        Assert.Contains("wanting a look", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Status_Should_Not_Flag_A_Healthy_Service()
    {
        var report = DiscordOperations.Status([Service("curator", alerts: 0, hoursAgo: 2)], now);

        Assert.DoesNotContain("wanting a look", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Status_Should_Say_When_A_Service_Is_Overdue()
    {
        // Nightly and last run 30 hours ago: due now, not "due in -6 h".
        var report = DiscordOperations.Status([Service("scout", alerts: 0, hoursAgo: 30)], now);

        Assert.Contains("due now", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Status_Should_Omit_Due_Time_For_A_Service_With_No_Recorded_Cadence()
    {
        var report = DiscordOperations.Status(
            [Service("mystery", alerts: 0, hoursAgo: 3, cadence: null)], now);

        Assert.DoesNotContain("due", report, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.5, "moments")]
    [InlineData(30, "30 min")]
    [InlineData(120, "2 h")]
    [InlineData(2880, "2 d")]
    public void Age_Should_Read_Compactly(double minutes, string expected)
    {
        Assert.Equal(expected, DiscordOperations.Age(TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void Age_Should_Not_Render_A_Negative_Span()
    {
        // Clock skew between the host and a recorded run produced "-1 h ago" once.
        Assert.Equal("moments", DiscordOperations.Age(TimeSpan.FromMinutes(-5)));
    }

    [Fact]
    public void Help_Should_Name_The_Commands_It_Actually_Handles()
    {
        var help = DiscordOperations.Help();

        Assert.Contains("status", help, StringComparison.Ordinal);
        Assert.Contains("help", help, StringComparison.Ordinal);
    }
}
