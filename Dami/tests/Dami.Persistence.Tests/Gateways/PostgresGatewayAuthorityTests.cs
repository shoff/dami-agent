using Dami.Persistence.Gateways;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Gateways;

/// <summary>
/// The charter forbids a second authoritative gateway: two bots on one token answer
/// every message twice and neither can see the other doing it.
/// </summary>
[Collection(DatabaseCollection.NAME)]
public sealed class PostgresGatewayAuthorityTests
{
    private const string GATEWAY = "discord";

    private readonly DatabaseFixture fixture;

    public PostgresGatewayAuthorityTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public async Task TryAcquireAsync_Should_Grant_The_First_Caller()
    {
        await this.fixture.ResetAsync();
        var authority = this.CreateAuthority();

        await using var lease = await authority.TryAcquireAsync(GATEWAY, CancellationToken.None);

        Assert.NotNull(lease);
    }

    [Fact]
    public async Task TryAcquireAsync_Should_Refuse_A_Second_Holder()
    {
        await this.fixture.ResetAsync();
        var authority = this.CreateAuthority();
        await using var first = await authority.TryAcquireAsync(GATEWAY, CancellationToken.None);

        var second = await authority.TryAcquireAsync(GATEWAY, CancellationToken.None);

        Assert.Null(second);
    }

    [Fact]
    public async Task TryAcquireAsync_Should_Hand_Over_After_The_Holder_Releases()
    {
        await this.fixture.ResetAsync();
        var authority = this.CreateAuthority();
        var first = await authority.TryAcquireAsync(GATEWAY, CancellationToken.None);
        await first!.DisposeAsync();

        await using var second = await authority.TryAcquireAsync(GATEWAY, CancellationToken.None);

        Assert.NotNull(second);
    }

    [Fact]
    public async Task TryAcquireAsync_Should_Not_Block_A_Different_Gateway()
    {
        await this.fixture.ResetAsync();
        var authority = this.CreateAuthority();
        await using var discord = await authority.TryAcquireAsync(GATEWAY, CancellationToken.None);

        await using var other = await authority.TryAcquireAsync("signal", CancellationToken.None);

        Assert.NotNull(other);
    }

    [Fact]
    public async Task TryAcquireAsync_Should_Record_Who_Holds_It()
    {
        await this.fixture.ResetAsync();
        var authority = this.CreateAuthority();
        await using var lease = await authority.TryAcquireAsync(GATEWAY, CancellationToken.None);

        await using var command = this.fixture.DataSource.CreateCommand(
            $"select holder_pid from {DatabaseFixture.SCHEMA}.gateway_authority where gateway_name = 'discord';");
        var pid = await command.ExecuteScalarAsync();

        Assert.Equal(Environment.ProcessId, pid);
    }

    private PostgresGatewayAuthority CreateAuthority()
    {
        return new PostgresGatewayAuthority(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }),
            NullLogger<PostgresGatewayAuthority>.Instance);
    }
}
