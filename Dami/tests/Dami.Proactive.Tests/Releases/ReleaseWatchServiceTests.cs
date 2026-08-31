using Dami.Contracts.Domains;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Proactive.Releases;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Releases;

public sealed class ReleaseWatchServiceTests
{
    private static readonly DateTimeOffset now = new(2026, 8, 30, 22, 0, 0, TimeSpan.Zero);

    private const string OLLAMA_ATOM = """
        <feed xmlns="http://www.w3.org/2005/Atom"><title>Release notes</title>
        <entry><title>v0.12.3</title><link rel="alternate" href="https://github.com/ollama/ollama/releases/tag/v0.12.3"/><updated>2026-08-28T10:00:00Z</updated></entry>
        <entry><title>v0.12.2</title><link rel="alternate" href="https://github.com/ollama/ollama/releases/tag/v0.12.2"/><updated>2026-08-14T10:00:00Z</updated></entry>
        </feed>
        """;

    private readonly IEgressClient egress = Substitute.For<IEgressClient>();
    private readonly IDomainFactStore store = Substitute.For<IDomainFactStore>();
    private readonly List<DomainFact> written = [];

    public ReleaseWatchServiceTests()
    {
        this.store.RecordAsync(Arg.Do<DomainFact>(this.written.Add), Arg.Any<CancellationToken>())
            .Returns(true);
        this.store.TimelineAsync("release", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(FactsAsync());
    }

    [Fact]
    public async Task Should_Surface_A_Release_Newer_Than_The_Baseline()
    {
        this.Answer("latest.txt", "595.90  595.90/NVIDIA-Linux-x86_64-595.90.run");
        var service = this.Service(Watch("nvidia-driver", "https://download.nvidia.com/XFree86/Linux-x86_64/latest.txt", "nvidia-latest", "595.84", "the 595.84 segfault crashes the GUI"));

        var result = await service.RunPassAsync(Context(), CancellationToken.None);

        var surfacing = Assert.Single(result.Surfacings);
        Assert.Contains("595.90", surfacing.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_Name_The_Baseline_So_The_Reader_Knows_Why_It_Matters()
    {
        this.Answer("latest.txt", "595.90");
        var service = this.Service(Watch("nvidia-driver", "https://x/latest.txt", "nvidia-latest", "595.84", "segfault"));

        var result = await service.RunPassAsync(Context(), CancellationToken.None);

        Assert.Contains("595.84", result.Surfacings[0].Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_Stay_Quiet_When_Latest_Equals_The_Baseline()
    {
        this.Answer("latest.txt", "595.84  595.84/NVIDIA-Linux-x86_64-595.84.run");
        var service = this.Service(Watch("nvidia-driver", "https://x/latest.txt", "nvidia-latest", "595.84", "segfault"));

        var result = await service.RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal((0, 0), (result.Surfacings.Count, this.written.Count));
    }

    [Fact]
    public async Task Should_Not_Resurface_A_Release_Already_On_Record()
    {
        this.Answer("latest.txt", "595.90");
        this.store.TimelineAsync("release", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(FactsAsync(Fact("nvidia-driver 595.90 — https://download.nvidia.com/XFree86/Linux-x86_64/latest.txt")));
        var service = this.Service(Watch("nvidia-driver", "https://download.nvidia.com/XFree86/Linux-x86_64/latest.txt", "nvidia-latest", "595.84", "segfault"));

        var result = await service.RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    [Fact]
    public async Task Should_Learn_A_Baselineless_Feed_Quietly_On_First_Sight()
    {
        // Without a baseline the first pass cannot tell news from history; it records
        // what exists and says nothing. Change surfaces from the second pass on.
        this.Answer("ollama", OLLAMA_ATOM);
        var service = this.Service(Watch("ollama", "https://github.com/ollama/ollama/releases.atom", "feed", "", ""));

        var result = await service.RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal((0, 2), (result.Surfacings.Count, this.written.Count));
    }

    [Fact]
    public async Task Should_Surface_A_New_Entry_Once_A_Baselineless_Feed_Is_Learned()
    {
        this.Answer("ollama", OLLAMA_ATOM);
        this.store.TimelineAsync("release", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(FactsAsync(Fact("ollama 0.12.2 — https://github.com/ollama/ollama/releases/tag/v0.12.2")));
        var service = this.Service(Watch("ollama", "https://github.com/ollama/ollama/releases.atom", "feed", "", ""));

        var result = await service.RunPassAsync(Context(), CancellationToken.None);

        Assert.Contains("0.12.3", Assert.Single(result.Surfacings).Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_Ignore_Pre_Release_Entries()
    {
        // ollama's feed carries v0.33.2 beside v0.33.2-rc1; an rc is not a fix Steve can
        // install, and its version number collides with the stable one it precedes.
        this.Answer("ollama", """
            <feed xmlns="http://www.w3.org/2005/Atom">
            <entry><title>v0.33.2-rc1</title><link rel="alternate" href="https://github.com/ollama/ollama/releases/tag/v0.33.2-rc1"/><updated>2026-08-27T10:00:00Z</updated></entry>
            <entry><title>v0.33.2</title><link rel="alternate" href="https://github.com/ollama/ollama/releases/tag/v0.33.2"/><updated>2026-08-28T10:00:00Z</updated></entry>
            </feed>
            """);
        var service = this.Service(Watch("ollama", "https://github.com/ollama/ollama/releases.atom", "feed", "", ""));

        await service.RunPassAsync(Context(), CancellationToken.None);

        Assert.EndsWith("/v0.33.2", Assert.Single(this.written).Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_Survive_A_Refused_Watch_And_Read_The_Rest()
    {
        this.egress.SendAsync(
                Arg.Is<EgressRequest>(request => request.Destination.AbsoluteUri.Contains("nvidia", StringComparison.Ordinal)),
                Arg.Any<CancellationToken>())
            .Returns<EgressResponse>(_ => throw new EgressRefusedException("host not allowlisted"));
        this.Answer("ollama", OLLAMA_ATOM);
        var service = this.Service(
            Watch("nvidia-driver", "https://download.nvidia.com/latest.txt", "nvidia-latest", "595.84", "segfault"),
            Watch("ollama", "https://github.com/ollama/ollama/releases.atom", "feed", "", ""));

        var result = await service.RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal((ProactiveStatus.Completed, 2), (result.Status, this.written.Count));
    }

    [Fact]
    public async Task Should_Cap_Surfacings_And_Keep_Recording_Facts()
    {
        // A feed carrying five newer releases at once is one event, not five alerts.
        var entries = string.Join("", Enumerable.Range(1, 5).Select(minor => $"""
            <entry><title>13.{minor}.0</title><link rel="alternate" href="https://github.com/AvaloniaUI/Avalonia/releases/tag/13.{minor}.0"/><updated>2026-08-2{minor}T10:00:00Z</updated></entry>
            """));
        this.Answer("Avalonia", $"""<feed xmlns="http://www.w3.org/2005/Atom">{entries}</feed>""");
        var service = this.Service(Watch("avalonia", "https://github.com/AvaloniaUI/Avalonia/releases.atom", "feed", "12.1.1", ""));

        var result = await service.RunPassAsync(Context(), CancellationToken.None);

        Assert.Equal((3, 5), (result.Surfacings.Count, this.written.Count));
    }

    private static ProactiveContext Context() => new(Guid.NewGuid(), now, null);

    private static ReleaseWatch Watch(
        string name, string url, string kind, string baseline, string reason) => new()
    {
        Name = name,
        Url = url,
        Kind = kind,
        Baseline = baseline,
        Reason = reason,
    };

    private static DomainFact Fact(string description) => new(
        Guid.NewGuid(), "release", new DateOnly(2026, 8, 14), "release", description,
        "release-watch", now);

    private static async IAsyncEnumerable<DomainFact> FactsAsync(params DomainFact[] facts)
    {
        foreach (var fact in facts)
        {
            yield return fact;
        }

        await Task.CompletedTask;
    }

    private void Answer(string urlPart, string body)
    {
        this.egress.SendAsync(
                Arg.Is<EgressRequest>(request => request.Destination.AbsoluteUri.Contains(urlPart, StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>())
            .Returns(new EgressResponse(200, body));
    }

    private ReleaseWatchService Service(params ReleaseWatch[] watches)
    {
        var options = new ReleaseWatchOptions { WatchDelaySeconds = 0 };
        options.Watches.Clear();
        foreach (var watch in watches)
        {
            options.Watches.Add(watch);
        }

        return new ReleaseWatchService(
            this.store, this.egress, Options.Create(options), new FakeTimeProvider(now),
            NullLogger<ReleaseWatchService>.Instance);
    }
}
