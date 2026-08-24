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
    public void FrontierVoice_Should_Name_No_One_But_Dami()
    {
        var provider = CreateProvider("/nonexistent/dami-identity.md");

        Assert.DoesNotContain("Steve", provider.FrontierVoice, StringComparison.OrdinalIgnoreCase);
    }

    private static FileIdentityProvider CreateProvider(string path)
    {
        return new FileIdentityProvider(
            Options.Create(new IdentityOptions { Path = path }),
            NullLogger<FileIdentityProvider>.Instance);
    }
}
