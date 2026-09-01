using Dami.Contracts.Domains;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Proactive.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Security;

public sealed class CveWatchServiceTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 30, 23, 0, 0, TimeSpan.Zero);

    private const string USN_RSS = """
        <rss version="2.0"><channel><title>Ubuntu security notices</title>
        <item><title>USN-7654-1: PostgreSQL vulnerabilities</title><link>https://ubuntu.com/security/notices/USN-7654-1</link><pubDate>Fri, 28 Aug 2026 10:00:00 +0000</pubDate></item>
        <item><title>USN-7655-1: Firefox vulnerabilities</title><link>https://ubuntu.com/security/notices/USN-7655-1</link><pubDate>Fri, 28 Aug 2026 11:00:00 +0000</pubDate></item>
        </channel></rss>
        """;

    private const string ADVISORIES = """
        [
          { "ghsa_id": "GHSA-aaaa-bbbb-cccc", "summary": "Npgsql protocol desync",
            "severity": "high", "html_url": "https://github.com/advisories/GHSA-aaaa-bbbb-cccc",
            "published_at": "2026-08-20T00:00:00Z",
            "vulnerabilities": [ { "package": { "ecosystem": "nuget", "name": "Npgsql" },
                                   "vulnerable_version_range": "< 9.9.9" } ] }
        ]
        """;

    private readonly IEgressClient egress = Substitute.For<IEgressClient>();
    private readonly IInstalledInventory inventory = Substitute.For<IInstalledInventory>();
    private readonly IDomainFactStore store = Substitute.For<IDomainFactStore>();
    private readonly List<DomainFact> written = [];

    public CveWatchServiceTests()
    {
        this.store.RecordAsync(Arg.Do<DomainFact>(this.written.Add), Arg.Any<CancellationToken>())
            .Returns(true);
        this.store.TimelineAsync("security", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(FactsAsync());
        this.inventory.SystemPackagesAsync(Arg.Any<CancellationToken>())
            .Returns([("postgresql-16", "16.15"), ("vim", "9.1")]);
        this.inventory.NugetPackagesAsync(Arg.Any<CancellationToken>())
            .Returns([("Npgsql", "8.0.5"), ("Avalonia", "12.1.1")]);
        this.Answer("ubuntu.com", USN_RSS);
        this.Answer("api.github.com", ADVISORIES);
    }

    [Fact]
    public async Task Should_Surface_A_Usn_Naming_An_Installed_Package()
    {
        var result = await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.Contains(result.Surfacings, surfacing =>
            surfacing.Title.Contains("PostgreSQL", StringComparison.Ordinal)
            && surfacing.Body.Contains("postgresql-16", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_Not_Surface_A_Usn_For_Software_Not_Installed()
    {
        var result = await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.DoesNotContain(result.Surfacings, surfacing =>
            surfacing.Title.Contains("Firefox", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_Surface_A_Nuget_Advisory_Covering_The_Resolved_Version()
    {
        var result = await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.Contains(result.Surfacings, surfacing =>
            surfacing.Title.Contains("Npgsql 8.0.5", StringComparison.Ordinal)
            && surfacing.Body.Contains("GHSA-aaaa-bbbb-cccc", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_Stay_Quiet_When_The_Installed_Version_Is_Already_Fixed()
    {
        this.inventory.NugetPackagesAsync(Arg.Any<CancellationToken>())
            .Returns([("Npgsql", "9.9.9")]);
        this.Answer("ubuntu.com", "<rss version=\"2.0\"><channel></channel></rss>");

        var result = await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal((0, 0), (result.Surfacings.Count, this.written.Count));
    }

    [Fact]
    public async Task Should_Not_Resurface_An_Advisory_Already_On_Record()
    {
        var first = await this.Service().RunPassAsync(Context(), CancellationToken.None);
        this.store.TimelineAsync("security", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(FactsAsync(this.written.ToArray()));

        var second = await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal((2, 0), (first.Surfacings.Count, second.Surfacings.Count));
    }

    [Fact]
    public async Task Should_Survive_A_Refused_Source_And_Read_The_Other()
    {
        this.egress.SendAsync(
                Arg.Is<EgressRequest>(request => request.Destination.Host == "ubuntu.com"),
                Arg.Any<CancellationToken>())
            .Returns<EgressResponse>(_ => throw new EgressRefusedException("host not allowlisted"));

        var result = await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal(
            (ProactiveStatus.Completed, 1),
            (result.Status, result.Surfacings.Count));
    }

    [Fact]
    public async Task Should_Not_Name_Any_Installed_Package_In_The_Query()
    {
        // This used to pass the resolved NuGet closure as `affects=`. Public package
        // names, no versions — but in aggregate the dependency manifest of a private
        // repository, sent nightly in stable order. Pull broad, match at home.
        var requests = new List<Uri>();
        this.egress.SendAsync(
                Arg.Do<EgressRequest>(request => requests.Add(request.Destination)),
                Arg.Any<CancellationToken>())
            .Returns(new EgressResponse(200, "[]"));

        await this.Service().RunPassAsync(Context(), CancellationToken.None);

        var advisories = requests.FindAll(uri => uri.Host == "api.github.com");
        Assert.All(advisories, uri => Assert.DoesNotContain(
            "Npgsql", uri.Query, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_Not_Send_Versions_In_The_Query()
    {
        var requests = new List<Uri>();
        this.egress.SendAsync(
                Arg.Do<EgressRequest>(request => requests.Add(request.Destination)),
                Arg.Any<CancellationToken>())
            .Returns(new EgressResponse(200, "[]"));

        await this.Service().RunPassAsync(Context(), CancellationToken.None);

        Assert.All(
            requests.FindAll(uri => uri.Host == "api.github.com"),
            uri => Assert.DoesNotContain("8.0.5", uri.Query, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_Query_By_Ecosystem_And_Publication_Window_Only()
    {
        var requests = new List<Uri>();
        this.egress.SendAsync(
                Arg.Do<EgressRequest>(request => requests.Add(request.Destination)),
                Arg.Any<CancellationToken>())
            .Returns(new EgressResponse(200, "[]"));

        await this.Service().RunPassAsync(Context(), CancellationToken.None);

        var advisories = requests.Find(uri => uri.Host == "api.github.com")!;
        Assert.Contains("ecosystem=nuget", advisories.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("affects=", advisories.Query, StringComparison.Ordinal);
    }

    private static ProactiveContext Context() => new(Guid.NewGuid(), now, null);

    private static async IAsyncEnumerable<DomainFact> FactsAsync(params DomainFact[] facts)
    {
        foreach (var fact in facts)
        {
            yield return fact;
        }

        await Task.CompletedTask;
    }

    private void Answer(string host, string body)
    {
        this.egress.SendAsync(
                Arg.Is<EgressRequest>(request => request.Destination.Host.Contains(host, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>())
            .Returns(new EgressResponse(200, body));
    }

    private CveWatchService Service()
    {
        return new CveWatchService(
            this.store, this.egress, this.inventory,
            Options.Create(new CveWatchOptions()), new FakeTimeProvider(now),
            NullLogger<CveWatchService>.Instance);
    }
}
