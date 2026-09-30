using Dami.Persistence.Runtime;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Runtime;

/// <summary>The pause switch against a live database (migration 049).</summary>
[Collection(DatabaseCollection.NAME)]
public sealed class PostgresPauseSwitchTests
{
    private static readonly DateTimeOffset now = new(2026, 9, 29, 16, 0, 0, TimeSpan.FromHours(-5));

    private readonly DatabaseFixture fixture;

    public PostgresPauseSwitchTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    private PostgresPauseSwitch Switch() =>
        new(this.fixture.DataSource, Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));

    [Fact]
    public async Task A_Timed_Pause_Should_Hold_Until_It_Ends_And_A_Second_Pause_Should_Replace_It()
    {
        await this.fixture.ResetAsync();
        var pause = this.Switch();
        Assert.Null(await pause.CurrentAsync(now, CancellationToken.None));

        await pause.PauseAsync(now.AddHours(1), "lending the GPU", now, CancellationToken.None);
        await pause.PauseAsync(now.AddHours(3), "longer", now, CancellationToken.None);

        Assert.Equal("longer", (await pause.CurrentAsync(now.AddHours(2), CancellationToken.None))!.Reason);
        Assert.Null(await pause.CurrentAsync(now.AddHours(4), CancellationToken.None));
    }

    [Fact]
    public async Task An_Open_Pause_Should_Hold_Until_Resumed()
    {
        await this.fixture.ResetAsync();
        var pause = this.Switch();
        await pause.PauseAsync(null, "away", now, CancellationToken.None);

        Assert.NotNull(await pause.CurrentAsync(now.AddDays(30), CancellationToken.None));
        await pause.ResumeAsync(CancellationToken.None);
        Assert.Null(await pause.CurrentAsync(now, CancellationToken.None));
    }
}
