using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Dami.Architecture.Tests;

/// <summary>Loads production assemblies by name, skipping those that do not exist yet.</summary>
/// <remarks>
/// Skipping is deliberate. These tests name the full intended solution from
/// dami-core-system-architecture.md §8, most of which is unwritten. A rule should start
/// guarding a project the moment it appears rather than after someone remembers to add
/// it here.
/// </remarks>
public static class AssemblyProbe
{
    /// <summary>Loads each named assembly that is present.</summary>
    public static IReadOnlyList<Assembly> Load(IEnumerable<string> assemblyNames)
    {
        ArgumentNullException.ThrowIfNull(assemblyNames);
        var loaded = new List<Assembly>();

        foreach (var name in assemblyNames)
        {
            var assembly = TryLoad(name);
            if (assembly is not null)
            {
                loaded.Add(assembly);
            }
        }

        return loaded;
    }

    private static Assembly? TryLoad(string name)
    {
        try
        {
            return Assembly.Load(new AssemblyName(name));
        }
        catch (Exception exception) when (exception is FileNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }
}
