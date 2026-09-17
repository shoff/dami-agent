using Dami.Proactive.Civic;
using Dami.Proactive.Recalls;
using Dami.Proactive.Releases;
using Dami.Proactive.Security;
using Dami.Proactive.Weather;
using Xunit;

namespace Dami.Proactive.Tests;

public sealed class ProactiveEgressDeploymentTests
{
    [Fact]
    public void Deploy_Should_Install_The_Proactive_Egress_Manifest()
    {
        var deploy = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "deploy.sh"));

        Assert.Contains("proactive-egress-hosts.txt", deploy, StringComparison.Ordinal);
    }

    [Fact]
    public void Deployment_Manifest_Should_Allow_Every_Default_Internet_Source()
    {
        var manifest = Path.Combine(RepositoryRoot(), "tools", "config", "proactive-egress-hosts.txt");
        var deployed = File.Exists(manifest)
            ? File.ReadAllLines(manifest).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        var missing = DefaultSourceHosts()
            .Where(host => !deployed.Contains(host))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0, $"deployment manifest is missing: {string.Join(", ", missing)}");
    }

    private static HashSet<string> DefaultSourceHosts()
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(hosts, new CivicFeedOptions().Feeds.Select(feed => feed.Url));
        Add(hosts, new ReleaseWatchOptions().Watches.Select(watch => watch.Url));
        var cve = new CveWatchOptions();
        Add(hosts, [cve.UsnFeedUrl, cve.AdvisoriesUrl]);
        var recalls = new RecallSentinelOptions();
        Add(hosts, [recalls.DrugUrl, recalls.DeviceUrl, recalls.CpscUrl]);
        var weather = new WeatherOptions();
        Add(hosts, [weather.ForecastUrl, weather.AlertsUrl]);
        return hosts;
    }

    private static void Add(HashSet<string> hosts, IEnumerable<string> urls)
    {
        foreach (var url in urls)
        {
            hosts.Add(new Uri(url).Host);
        }
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "tools", "deploy.sh")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("could not find the repository root");
    }
}
