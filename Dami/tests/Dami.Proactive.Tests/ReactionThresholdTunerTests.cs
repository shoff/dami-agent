using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests;

/// <summary>H8: bounded, stateless, and silence moves nothing.</summary>
public sealed class ReactionThresholdTunerTests
{
    private const string SERVICE = "interest-scout";
    private const double BASE = 0.55;

    private readonly ISurfacingQueue surfacingQueue = Substitute.For<ISurfacingQueue>();
    private readonly List<SurfacingReaction> reactions = [];

    [Fact]
    public async Task EffectiveThresholdAsync_Should_Return_The_Base_With_No_Reactions()
    {
        Assert.Equal(BASE, await this.TuneAsync());
    }

    [Fact]
    public async Task EffectiveThresholdAsync_Should_Return_The_Base_Below_Minimum_Evidence()
    {
        this.React("bad: noise", count: 4);

        Assert.Equal(BASE, await this.TuneAsync());
    }

    [Fact]
    public async Task EffectiveThresholdAsync_Should_Raise_When_Reactions_Lean_Bad()
    {
        this.React("bad: noise", count: 8);
        this.React("good: useful", count: 2);

        Assert.True(await this.TuneAsync() > BASE);
    }

    [Fact]
    public async Task EffectiveThresholdAsync_Should_Lower_When_Reactions_Lean_Good()
    {
        this.React("good: useful", count: 8);
        this.React("bad: noise", count: 2);

        Assert.True(await this.TuneAsync() < BASE);
    }

    [Fact]
    public async Task EffectiveThresholdAsync_Should_Never_Rise_Past_The_Ceiling()
    {
        this.React("bad: noise", count: 30);

        Assert.Equal(BASE + 0.25, await this.TuneAsync(gain: 5.0), precision: 6);
    }

    [Fact]
    public async Task EffectiveThresholdAsync_Should_Never_Drop_Past_The_Floor()
    {
        this.React("good: useful", count: 30);

        Assert.Equal(BASE - 0.10, await this.TuneAsync(gain: 5.0), precision: 6);
    }

    [Fact]
    public async Task EffectiveThresholdAsync_Should_Ignore_Meh_For_The_Lean_But_Count_It_As_Evidence()
    {
        this.React("meh", count: 10);

        Assert.Equal(BASE, await this.TuneAsync());
    }

    private void React(string feedback, int count)
    {
        for (var index = 0; index < count; index++)
        {
            this.reactions.Add(new SurfacingReaction($"item {this.reactions.Count}", feedback));
        }
    }

    private async Task<double> TuneAsync(double gain = 0.2)
    {
        this.surfacingQueue
            .ReactionsForServiceAsync(SERVICE, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(AsAsync(this.reactions));
        var tuner = new ReactionThresholdTuner(
            this.surfacingQueue,
            Options.Create(new ThresholdTuningOptions { Gain = gain }),
            NullLogger<ReactionThresholdTuner>.Instance);

        return await tuner.EffectiveThresholdAsync(SERVICE, BASE, CancellationToken.None);
    }

    private static async IAsyncEnumerable<SurfacingReaction> AsAsync(List<SurfacingReaction> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }
}
