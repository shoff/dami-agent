using System.Text.Json;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class NetworkActivityTests
{
    private static JsonDocument Facts(params (string AsOf, string Category, string Description)[] rows)
    {
        // Shaped like /domains/network: newest sweep first, as the store orders it.
        var items = rows
            .OrderByDescending(row => row.AsOf)
            .Select(row => $$"""
                {"factId":"{{Guid.NewGuid()}}","domain":"network","asOf":"{{row.AsOf}}",
                 "category":"{{row.Category}}","description":"{{row.Description}}",
                 "source":"network-collector","recordedAt":"2026-08-30T12:00:00+00:00"}
                """);
        return JsonDocument.Parse("[" + string.Join(",", items) + "]");
    }

    [Fact]
    public void Latest_Should_Take_Only_The_Newest_Sweep()
    {
        using var facts = Facts(
            ("2026-08-30", "device", "gateway (192.168.4.1) answers ping"),
            ("2026-08-29", "device", "old-box (192.168.4.9) answers ping"));

        var rows = NetworkActivity.Latest(facts.RootElement);

        Assert.Equal("gateway (192.168.4.1) answers ping", Assert.Single(rows).Description);
    }

    [Fact]
    public void Latest_Should_Put_Problems_Before_Healthy_Rows()
    {
        using var facts = Facts(
            ("2026-08-30", "service", "postgresql on 127.0.0.1:5432 is listening"),
            ("2026-08-30", "interface", "Interface eno1 is down (no IPv4 address)"));

        var rows = NetworkActivity.Latest(facts.RootElement);

        Assert.True(rows[0].IsProblem);
    }

    [Theory]
    [InlineData("Interface eno1 is down (no IPv4 address)", true)]
    [InlineData("mac-mini (192.168.4.23) does not answer ping", true)]
    [InlineData("dami-host on 127.0.0.1:5810 is not listening", true)]
    [InlineData("gateway (192.168.4.1) answers ping", false)]
    [InlineData("postgresql on 127.0.0.1:5432 is listening", false)]
    public void IsProblem_Should_Recognise_The_Collector_Phrasings(string description, bool expected)
    {
        Assert.Equal(expected, NetworkActivity.IsProblem(description));
    }

    [Fact]
    public void Changes_Should_Report_What_Appeared_And_What_Vanished()
    {
        using var facts = Facts(
            ("2026-08-30", "device", "new-phone (192.168.4.87) answers ping"),
            ("2026-08-30", "device", "gateway (192.168.4.1) answers ping"),
            ("2026-08-29", "device", "gateway (192.168.4.1) answers ping"),
            ("2026-08-29", "device", "old-box (192.168.4.9) answers ping"));

        var changes = NetworkActivity.Changes(facts.RootElement);

        Assert.Equal(
            [("appeared", "new-phone (192.168.4.87) answers ping"),
             ("gone", "old-box (192.168.4.9) answers ping")],
            changes.Select(change => (change.Kind, change.Description)).ToList());
    }

    [Fact]
    public void Changes_Should_Be_Empty_When_There_Is_Only_One_Sweep()
    {
        using var facts = Facts(("2026-08-30", "device", "gateway (192.168.4.1) answers ping"));

        Assert.Empty(NetworkActivity.Changes(facts.RootElement));
    }

    [Fact]
    public void Tiles_Should_Count_Problems_Against_The_Previous_Sweep()
    {
        using var facts = Facts(
            ("2026-08-30", "interface", "Interface eno1 is down (no IPv4 address)"),
            ("2026-08-30", "interface", "Interface eno2 is down (no IPv4 address)"),
            ("2026-08-29", "interface", "Interface eno1 is down (no IPv4 address)"));

        var tiles = NetworkActivity.Tiles(facts.RootElement);

        var problems = Assert.Single(tiles, tile => tile.Label == "PROBLEMS");
        Assert.Equal(("2", "1 the sweep before"), (problems.Value, problems.Detail));
    }

    [Fact]
    public void ProblemsBySweep_Should_Order_Sweeps_Oldest_First()
    {
        using var facts = Facts(
            ("2026-08-30", "interface", "Interface eno1 is down (no IPv4 address)"),
            ("2026-08-28", "device", "gateway (192.168.4.1) answers ping"));

        var points = NetworkActivity.ProblemsBySweep(facts.RootElement);

        Assert.Equal([0d, 1d], points.Select(point => point.Value).ToList());
    }

    [Fact]
    public void AnalysisPrompt_Should_Carry_The_Facts_And_Demand_Labeled_Speculation()
    {
        using var facts = Facts(
            ("2026-08-30", "device", "gateway (192.168.4.1) answers ping"));

        var prompt = NetworkActivity.AnalysisPrompt(facts.RootElement);

        Assert.Equal(
            (true, true),
            (prompt.Contains("gateway (192.168.4.1) answers ping", StringComparison.Ordinal),
                prompt.Contains("speculation", StringComparison.OrdinalIgnoreCase)));
    }
}
