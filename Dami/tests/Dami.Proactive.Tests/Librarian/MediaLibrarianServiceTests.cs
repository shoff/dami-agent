using System.Text.Json;
using Dami.Contracts.Models;
using Dami.Contracts.Proactive;
using NSubstitute;
using Dami.Proactive.Librarian;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dami.Proactive.Tests.Librarian;

/// <summary>Propose-only, proven against a real temp directory.</summary>
public sealed class MediaLibrarianServiceTests : IDisposable
{
    private static readonly DateTimeOffset now = new(2026, 8, 23, 4, 0, 0, TimeSpan.Zero);

    private readonly string root;
    private readonly string manifests;

    public MediaLibrarianServiceTests()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "dami-librarian-" + Guid.NewGuid().ToString("N"));
        this.root = Path.Combine(scratch, "root");
        this.manifests = Path.Combine(scratch, "manifests");
        Directory.CreateDirectory(this.root);
    }

    public void Dispose()
    {
        Directory.Delete(Path.GetDirectoryName(this.root)!, recursive: true);
    }

    [Fact]
    public async Task RunPassAsync_Should_Stay_Quiet_Below_The_Floor()
    {
        this.Seed("one.jpg");

        var result = await this.CreateService(minimum: 5).RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    [Fact]
    public async Task RunPassAsync_Should_Surface_One_Manifest_For_Loose_Files()
    {
        this.SeedMany();

        var result = await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Single(result.Surfacings);
    }

    [Fact]
    public async Task RunPassAsync_Should_Write_A_Manifest_That_Parses()
    {
        this.SeedMany();

        await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        var manifestFile = Assert.Single(Directory.GetFiles(this.manifests));
        var manifest = JsonSerializer.Deserialize<MediaLibrarianService.Manifest>(
            await File.ReadAllTextAsync(manifestFile));
        Assert.Equal(3, manifest!.Proposals.Count);
    }

    [Fact]
    public async Task RunPassAsync_Should_Move_Nothing()
    {
        var seeded = this.SeedMany();

        await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.All(seeded, file => Assert.True(File.Exists(file), $"{file} was touched"));
    }

    [Fact]
    public async Task RunPassAsync_Should_Create_No_Directories_Under_The_Root()
    {
        this.SeedMany();

        await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(Directory.GetDirectories(this.root));
    }

    [Fact]
    public async Task RunPassAsync_Should_Ignore_Files_Already_In_Subdirectories()
    {
        this.SeedMany();
        Directory.CreateDirectory(Path.Combine(this.root, "sorted"));
        await File.WriteAllTextAsync(Path.Combine(this.root, "sorted", "organized.jpg"), "x");

        await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        var manifestFile = Assert.Single(Directory.GetFiles(this.manifests));
        Assert.DoesNotContain("organized.jpg", await File.ReadAllTextAsync(manifestFile));
    }

    [Fact]
    public async Task RunPassAsync_Should_Group_By_Kind_And_Month()
    {
        this.SeedMany();

        await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        var manifest = JsonSerializer.Deserialize<MediaLibrarianService.Manifest>(
            await File.ReadAllTextAsync(Directory.GetFiles(this.manifests)[0]));
        var photo = manifest!.Proposals.First(proposal => proposal.From.EndsWith("a.jpg"));
        Assert.Contains(Path.Combine("photos"), photo.To);
    }

    [Fact]
    public async Task RunPassAsync_Should_Skip_Unknown_Extensions()
    {
        this.SeedMany();
        this.Seed("mystery.xyz");

        await this.CreateService().RunPassAsync(Context(), CancellationToken.None);

        var manifestFile = Assert.Single(Directory.GetFiles(this.manifests));
        Assert.DoesNotContain("mystery.xyz", await File.ReadAllTextAsync(manifestFile));
    }

    [Fact]
    public async Task RunPassAsync_Should_Stay_Quiet_With_No_Roots_Configured()
    {
        var service = new MediaLibrarianService(
            Substitute.For<IVisionClient>(),
            Options.Create(new MediaLibrarianOptions { ManifestDirectory = this.manifests }),
            new FakeTimeProvider(now), NullLogger<MediaLibrarianService>.Instance);

        var result = await service.RunPassAsync(Context(), CancellationToken.None);

        Assert.Empty(result.Surfacings);
    }

    private string Seed(string name)
    {
        var path = Path.Combine(this.root, name);
        File.WriteAllText(path, "content");
        return path;
    }

    private List<string> SeedMany()
    {
        return [this.Seed("a.jpg"), this.Seed("b.mp4"), this.Seed("c.pdf")];
    }

    private static ProactiveContext Context()
    {
        return new ProactiveContext(Guid.NewGuid(), now, null);
    }

    private readonly IVisionClient visionClient = Substitute.For<IVisionClient>();

    [Fact]
    public async Task RunPassAsync_Should_Enrich_Image_Proposals_When_Vision_Is_Enabled()
    {
        this.SeedMany();
        this.visionClient.DescribeAsync(
                Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("a finished spitfire on the bench; tags: model, aircraft, hobby");

        await this.CreateService(vision: true).RunPassAsync(Context(), CancellationToken.None);

        var manifest = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(this.manifests)));
        Assert.Contains("a finished spitfire on the bench", manifest);
    }

    [Fact]
    public async Task RunPassAsync_Should_Keep_The_Plain_Proposal_When_Vision_Fails()
    {
        this.SeedMany();
        this.visionClient.DescribeAsync(
                Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new HttpRequestException("sidecar down"));

        var result = await this.CreateService(vision: true).RunPassAsync(Context(), CancellationToken.None);

        Assert.Single(result.Surfacings);
    }

    private MediaLibrarianService CreateService(int minimum = 3, bool vision = false)
    {
        var options = new MediaLibrarianOptions
        {
            ManifestDirectory = this.manifests,
            MinimumLooseFiles = minimum,
            VisionEnabled = vision,
        };
        options.RootPaths.Add(this.root);

        return new MediaLibrarianService(
            this.visionClient, Options.Create(options),
            new FakeTimeProvider(now), NullLogger<MediaLibrarianService>.Instance);
    }
}
