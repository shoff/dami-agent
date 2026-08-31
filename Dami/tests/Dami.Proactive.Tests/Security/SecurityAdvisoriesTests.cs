using Dami.Proactive.Security;
using Xunit;

namespace Dami.Proactive.Tests.Security;

public sealed class SecurityAdvisoriesTests
{
    private const string GITHUB_JSON = """
        [
          {
            "ghsa_id": "GHSA-aaaa-bbbb-cccc",
            "summary": "Npgsql SQL injection via protocol desync",
            "severity": "high",
            "html_url": "https://github.com/advisories/GHSA-aaaa-bbbb-cccc",
            "published_at": "2026-08-20T00:00:00Z",
            "vulnerabilities": [
              { "package": { "ecosystem": "nuget", "name": "Npgsql" },
                "vulnerable_version_range": "< 8.0.3" },
              { "package": { "ecosystem": "npm", "name": "left-pad" },
                "vulnerable_version_range": "< 1.0.0" }
            ]
          }
        ]
        """;

    [Fact]
    public void ParseGithub_Should_Read_Only_The_Nuget_Vulnerabilities()
    {
        var advisories = SecurityAdvisories.ParseGithub(GITHUB_JSON);

        var advisory = Assert.Single(advisories);
        Assert.Equal(
            ("GHSA-aaaa-bbbb-cccc", "Npgsql", "< 8.0.3", "high"),
            (advisory.GhsaId, advisory.Package, advisory.Range, advisory.Severity));
    }

    [Fact]
    public void ParseGithub_Should_Yield_Nothing_For_Garbage()
    {
        Assert.Empty(SecurityAdvisories.ParseGithub("not json"));
    }

    [Theory]
    [InlineData("USN-7654-1: OpenSSL vulnerabilities", "openssl")]
    [InlineData("USN-7660-1: PostgreSQL vulnerabilities", "postgresql-16")]
    [InlineData("USN-7661-1: Docker vulnerability", "docker.io")]
    public void UsnMentions_Should_Match_An_Installed_Package(string title, string installed)
    {
        Assert.Equal(installed, SecurityAdvisories.UsnMentions(title, [installed, "vim"]));
    }

    [Fact]
    public void UsnMentions_Should_Stay_Quiet_For_Software_Not_Installed()
    {
        Assert.Null(SecurityAdvisories.UsnMentions(
            "USN-7655-1: Firefox vulnerabilities", ["openssl", "postgresql-16"]));
    }

    [Fact]
    public void UsnMentions_Should_Not_Match_On_Boilerplate_Words()
    {
        // Every notice says "vulnerabilities"; a package unluckily named close to a
        // boilerplate word must not turn every notice into an alert.
        Assert.Null(SecurityAdvisories.UsnMentions(
            "USN-7656-1: Linux kernel vulnerabilities", ["vulnerability-scanner", "linux-image-generic"]));
    }
}
