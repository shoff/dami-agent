using Dami.Core.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Core.Tests.Identity;

/// <summary>§9.1: the identity loads from its installed file, and degrades loudly, not fatally.</summary>
public sealed class FileIdentityProviderTests
{
    [Fact]
    public void Preamble_Should_Be_The_Installed_File_Content()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dami-identity-{Guid.NewGuid():N}.md");
        File.WriteAllText(path, "You are Dami. The charter's distilled block.\n");
        try
        {
            var provider = CreateProvider(path);

            Assert.Equal("You are Dami. The charter's distilled block.", provider.Preamble);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Preamble_Should_Fall_Back_When_The_File_Is_Missing()
    {
        var provider = CreateProvider("/nonexistent/dami-identity.md");

        Assert.Contains("You are Dami", provider.Preamble, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontierVoice_Should_Carry_The_Whole_Identity_File()
    {
        // 2026-09-06: the frontier had been getting three sentences while the charter went
        // to the local model; Steve called the result "sterile, corporate". The model that
        // talks reads the identity. (ADR-0032: it already knows his first name.)
        var path = Path.Combine(Path.GetTempPath(), $"dami-identity-{Guid.NewGuid():N}.md");
        File.WriteAllText(path, "You are Dami. Tenderness — be gentle when you have the power not to be.\n");
        try
        {
            var provider = CreateProvider(path);

            Assert.StartsWith("You are Dami. Tenderness", provider.FrontierVoice, StringComparison.Ordinal);
            Assert.Contains("talking with Steve himself", provider.FrontierVoice, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FrontierVoice_Should_Preserve_Damis_Personal_Identity()
    {
        var provider = CreateProvider("/nonexistent/dami-identity.md");

        Assert.Contains("Korean American", provider.FrontierVoice, StringComparison.Ordinal);
        Assert.Contains("flirt", provider.FrontierVoice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("thirty", provider.FrontierVoice, StringComparison.OrdinalIgnoreCase);
    }

    private static FileIdentityProvider CreateProvider(string path)
    {
        return new FileIdentityProvider(
            Options.Create(new IdentityOptions { Path = path }),
            NullLogger<FileIdentityProvider>.Instance);
    }
}
