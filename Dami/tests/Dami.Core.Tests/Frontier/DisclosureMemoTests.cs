using Dami.Contracts.Privacy;
using Dami.Core.Frontier;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dami.Core.Tests.Frontier;

public sealed class DisclosureMemoTests
{
    private static readonly DateTimeOffset start = new(2026, 9, 5, 14, 0, 0, TimeSpan.Zero);

    private static DisclosedItem Pass(string line) => new(line, Disclosure.Pass, line, "ok");

    [Fact]
    public async Task Decide_Should_Ask_Only_For_Unknown_Lines_And_Keep_Order()
    {
        var memo = new DisclosureMemo(new FakeTimeProvider(start));
        memo.Remember([Pass("a")]);
        var asked = new List<string>();

        var decided = await memo.DecideAsync(["a", "b", "a", "c"], TimeSpan.FromMinutes(30),
            fresh => { asked.AddRange(fresh); return Task.FromResult<IReadOnlyList<DisclosedItem>>(fresh.Select(Pass).ToList()); },
            CancellationToken.None);

        Assert.Equal(["b", "c"], asked);
        Assert.Equal(["a", "b", "a", "c"], decided.Select(item => item.Original));
    }

    [Fact]
    public async Task A_Verdict_Should_Expire_After_Its_Lifetime()
    {
        var clock = new FakeTimeProvider(start);
        var memo = new DisclosureMemo(clock);
        memo.Remember([Pass("a")]);
        clock.Advance(TimeSpan.FromMinutes(31));
        var asked = new List<string>();

        await memo.DecideAsync(["a"], TimeSpan.FromMinutes(30),
            fresh => { asked.AddRange(fresh); return Task.FromResult<IReadOnlyList<DisclosedItem>>(fresh.Select(Pass).ToList()); },
            CancellationToken.None);

        Assert.Equal(["a"], asked);
    }

    [Fact]
    public async Task Forget_Should_Make_A_Corrected_Line_Be_Judged_Again()
    {
        var memo = new DisclosureMemo(new FakeTimeProvider(start));
        memo.Remember([Pass("Steve's surgeon is Dr Harrison")]);
        memo.Forget("Steve's surgeon is Dr Harrison");
        var asked = new List<string>();

        await memo.DecideAsync(["Steve's surgeon is Dr Harrison"], TimeSpan.FromMinutes(30),
            fresh => { asked.AddRange(fresh); return Task.FromResult<IReadOnlyList<DisclosedItem>>(fresh.Select(Pass).ToList()); },
            CancellationToken.None);

        Assert.Single(asked);
    }

    [Fact]
    public async Task A_Zero_Lifetime_Should_Judge_Everything_Every_Time()
    {
        var memo = new DisclosureMemo(new FakeTimeProvider(start));
        memo.Remember([Pass("a")]);
        var asked = new List<string>();

        await memo.DecideAsync(["a"], TimeSpan.Zero,
            fresh => { asked.AddRange(fresh); return Task.FromResult<IReadOnlyList<DisclosedItem>>(fresh.Select(Pass).ToList()); },
            CancellationToken.None);

        Assert.Equal(["a"], asked);
    }

    [Fact]
    public async Task A_Line_The_Gate_Forgot_To_Rule_On_Should_Be_Withheld()
    {
        // Fail closed: a missing verdict is not a pass.
        var memo = new DisclosureMemo(new FakeTimeProvider(start));

        var decided = await memo.DecideAsync(["a", "b"], TimeSpan.FromMinutes(30),
            fresh => Task.FromResult<IReadOnlyList<DisclosedItem>>([Pass("a")]), CancellationToken.None);

        Assert.Equal(Disclosure.Withhold, decided[1].Disclosure);
    }
}
