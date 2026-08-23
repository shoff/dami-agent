using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace Dami.Architecture.Tests;

/// <summary>
/// The solution's project-reference graph, read from the .csproj files on disk.
/// </summary>
/// <remarks>
/// Reading the files rather than loaded assemblies is deliberate. It covers every
/// project in the solution instead of only those this test project references, and it
/// fails the moment a forbidden ProjectReference is added rather than when someone
/// happens to consume it.
/// </remarks>
public sealed class SolutionGraph
{
    private const string SOLUTION_FILE = "Dami.sln";

    private readonly Dictionary<string, IReadOnlyList<string>> referencesByProject;

    private SolutionGraph(Dictionary<string, IReadOnlyList<string>> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        this.referencesByProject = references;
    }

    /// <summary>Every project in the solution, by assembly-style name.</summary>
    public IReadOnlyCollection<string> Projects => this.referencesByProject.Keys;

    /// <summary>The projects that <paramref name="project"/> references directly.</summary>
    public IReadOnlyList<string> ReferencesOf(string project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return this.referencesByProject.TryGetValue(project, out var found)
            ? found
            : Array.Empty<string>();
    }

    /// <summary>Loads the graph by walking up to the directory holding the solution.</summary>
    public static SolutionGraph Load()
    {
        var root = FindSolutionRoot();
        var references = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var projectFile in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
        {
            var name = Path.GetFileNameWithoutExtension(projectFile);
            references[name] = ReadReferences(projectFile);
        }

        return new SolutionGraph(references);
    }

    private static IReadOnlyList<string> ReadReferences(string projectFile)
    {
        var found = new List<string>();

        foreach (var element in XDocument.Load(projectFile).Descendants("ProjectReference"))
        {
            var include = element.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include))
            {
                continue;
            }

            found.Add(Path.GetFileNameWithoutExtension(include.Replace('\\', '/')));
        }

        return found;
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SOLUTION_FILE)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate {SOLUTION_FILE} above {AppContext.BaseDirectory}.");
    }
}
