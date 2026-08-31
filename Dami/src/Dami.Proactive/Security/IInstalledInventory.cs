using System.Diagnostics;
using System.Text.Json;

namespace Dami.Proactive.Security;

/// <summary>What this host runs, read locally. The inventory itself never leaves.</summary>
public interface IInstalledInventory
{
    /// <summary>Installed system packages, name and version, from dpkg.</summary>
    Task<IReadOnlyList<(string Name, string Version)>> SystemPackagesAsync(
        CancellationToken cancellationToken);

    /// <summary>The repository's resolved NuGet closure, from project.assets.json files.</summary>
    Task<IReadOnlyList<(string Name, string Version)>> NugetPackagesAsync(
        CancellationToken cancellationToken);
}

/// <summary>The real inventory: dpkg for the system, the repo's asset files for NuGet.</summary>
/// <remarks>
/// Reads only. Notably it does not shell out to <c>dotnet list package --vulnerable</c>,
/// which would phone the registry from outside the egress boundary; the assets files on
/// disk already hold the resolved closure.
/// </remarks>
public sealed class LocalInstalledInventory : IInstalledInventory
{
    private readonly string repositoryRoot;

    /// <summary>Creates the inventory over a repository checkout.</summary>
    public LocalInstalledInventory(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        this.repositoryRoot = repositoryRoot;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(string Name, string Version)>> SystemPackagesAsync(
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dpkg-query",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        process.StartInfo.ArgumentList.Add("-W");
        process.StartInfo.ArgumentList.Add("-f=${Package} ${Version}\n");
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken)
            .ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode == 0 ? Parse(output) : [];
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<(string Name, string Version)>> NugetPackagesAsync(
        CancellationToken cancellationToken)
    {
        var packages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assets in Directory.EnumerateFiles(
            this.repositoryRoot, "project.assets.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadAssets(assets, packages);
        }

        var results = new List<(string, string)>(packages.Count);
        foreach (var package in packages)
        {
            results.Add((package.Key, package.Value));
        }

        return Task.FromResult<IReadOnlyList<(string, string)>>(results);
    }

    private static IReadOnlyList<(string, string)> Parse(string output)
    {
        var packages = new List<(string, string)>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var space = line.IndexOf(' ');
            if (space > 0)
            {
                packages.Add((line[..space], line[(space + 1)..]));
            }
        }

        return packages;
    }

    private static void ReadAssets(string path, Dictionary<string, string> packages)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("libraries", out var libraries))
            {
                return;
            }

            foreach (var library in libraries.EnumerateObject())
            {
                // Keys are "Name/Version"; only type=package rows are dependencies.
                var slash = library.Name.IndexOf('/');
                if (slash > 0
                    && library.Value.TryGetProperty("type", out var type)
                    && type.GetString() == "package")
                {
                    packages[library.Name[..slash]] = library.Name[(slash + 1)..];
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            // One unreadable assets file must not blind the whole inventory.
        }
    }
}
