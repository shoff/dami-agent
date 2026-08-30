using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dami.Authentication.Tests;

public sealed class DamiTokenStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(), $"dami-token-{Guid.NewGuid():N}");

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));

    private DamiTokenStore Store() => new(Path.Combine(this.directory, "token.json"), this.clock);

    public void Dispose()
    {
        if (Directory.Exists(this.directory))
        {
            Directory.Delete(this.directory, recursive: true);
        }
    }

    [Fact]
    public void Read_Should_Return_Null_When_Nothing_Is_Stored()
    {
        Assert.Null(this.Store().Read());
    }

    [Fact]
    public void Write_Then_Read_Should_Round_Trip()
    {
        var store = this.Store();

        store.Write(new DamiToken("at-1", "rt-1", TimeSpan.FromHours(1)));
        var read = store.Read();

        Assert.NotNull(read);
        Assert.Equal("at-1", read.AccessToken);
        Assert.Equal("rt-1", read.RefreshToken);
    }

    [Fact]
    public void Write_Should_Stamp_When_It_Was_Obtained()
    {
        // Expiry cannot be judged from a duration alone; without the stamp a stored token
        // looks fresh forever.
        var store = this.Store();

        store.Write(new DamiToken("at-1", null, TimeSpan.FromHours(1)));

        Assert.Equal(this.clock.GetUtcNow(), store.Read()!.ObtainedAt);
    }

    [Fact]
    public void Write_Should_Make_The_File_Readable_Only_By_Its_Owner()
    {
        // A bearer token readable by every account on the box is a credential leak that
        // looks like a working login.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var store = this.Store();
        store.Write(new DamiToken("at-1", null, TimeSpan.FromHours(1)));

        var mode = File.GetUnixFileMode(store.Location);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    [Fact]
    public void Read_Should_Treat_A_Corrupt_File_As_No_Token()
    {
        var store = this.Store();
        Directory.CreateDirectory(this.directory);
        File.WriteAllText(store.Location, "{ not json");

        Assert.Null(store.Read());
    }

    [Fact]
    public void Clear_Should_Forget_The_Token()
    {
        var store = this.Store();
        store.Write(new DamiToken("at-1", null, TimeSpan.FromHours(1)));

        store.Clear();

        Assert.Null(store.Read());
    }

    [Fact]
    public void Clear_Should_Be_Safe_When_There_Is_Nothing_To_Clear()
    {
        this.Store().Clear();
    }

    [Fact]
    public void A_Token_Should_Expire_A_Minute_Early()
    {
        // Slack for a turn already in flight: a token that expires mid-request fails the
        // request rather than the login.
        var token = new DamiToken("at", null, TimeSpan.FromHours(1))
        {
            ObtainedAt = this.clock.GetUtcNow(),
        };

        Assert.False(token.IsExpiredAt(this.clock.GetUtcNow().AddMinutes(58)));
        Assert.True(token.IsExpiredAt(this.clock.GetUtcNow().AddMinutes(59)));
    }

    [Fact]
    public void A_Token_With_No_Stamp_Should_Not_Be_Called_Expired()
    {
        // Tokens written before stamping existed must not all read as expired at once.
        Assert.False(new DamiToken("at", null, TimeSpan.FromHours(1))
            .IsExpiredAt(this.clock.GetUtcNow().AddYears(1)));
    }
}
