using Dami.Contracts.Privacy;
using Dami.Core.Frontier;
using Xunit;

namespace Dami.Core.Tests.Frontier;

/// <summary>
/// Every local line a turn put before the gate, with its verdict — including the ones the
/// memo answered, which the ledger never sees again.
/// </summary>
public sealed class TurnDisclosuresTests
{
    private static DisclosedItem Item(string line, Disclosure disclosure = Disclosure.Pass) =>
        new(line, disclosure, disclosure == Disclosure.Withhold ? string.Empty : line, "why");

    [Fact]
    public void A_Remembered_Turn_Should_Be_Read_Back_Whole()
    {
        var disclosures = new TurnDisclosures();
        var trace = Guid.NewGuid();

        disclosures.Remember(trace, [Item("a"), Item("b", Disclosure.Withhold)]);

        Assert.Equal(["a", "b"], disclosures.For(trace)!.Select(item => item.Original));
    }

    [Fact]
    public void An_Unknown_Turn_Should_Be_Null()
    {
        Assert.Null(new TurnDisclosures().For(Guid.NewGuid()));
    }

    [Fact]
    public void Only_The_Most_Recent_Turns_Should_Be_Kept()
    {
        var disclosures = new TurnDisclosures();
        var first = Guid.NewGuid();
        disclosures.Remember(first, [Item("old")]);

        for (var turn = 0; turn < TurnDisclosures.KEPT; turn++)
        {
            disclosures.Remember(Guid.NewGuid(), [Item("new")]);
        }

        Assert.Null(disclosures.For(first));
    }
}
