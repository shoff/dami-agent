using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Dami.Architecture.Tests;

/// <summary>
/// Dependency-direction rules from docs/csharpcodestandards.md §7 and
/// dami-core-system-architecture.md §8.
/// </summary>
/// <remarks>
/// These close the two failure modes §6 names but no analyzer catches: abstractions at
/// the wrong layer, and a lower layer reaching upward. A violation here is an
/// architectural regression, not a style preference.
/// </remarks>
public sealed class LayeringTests
{
    private const string CONTRACTS = "Dami.Contracts";
    private const string CORE = "Dami.Core";

    private static readonly SolutionGraph graph = SolutionGraph.Load();

    [Fact]
    public void Contracts_Should_Depend_On_Nothing()
    {
        var offenders = graph.ReferencesOf(CONTRACTS);

        Assert.True(
            offenders.Count == 0,
            $"{CONTRACTS} must have no project references. Found: {Describe(offenders)}");
    }

    [Fact]
    public void Core_Should_Depend_Only_On_Contracts()
    {
        var offenders = graph.ReferencesOf(CORE)
            .Where(reference => reference != CONTRACTS)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"{CORE} may reference only {CONTRACTS}. It defines the abstractions it needs; "
            + $"implementations depend on it, never the reverse. Found: {Describe(offenders)}");
    }

    [Fact]
    public void Nothing_Outside_A_Composition_Root_Should_Reference_A_Host()
    {
        var offenders = new List<string>();

        foreach (var project in ProductionProjects().Where(candidate => !IsHost(candidate)))
        {
            offenders.AddRange(graph.ReferencesOf(project)
                .Where(IsHost)
                .Select(reference => $"{project} -> {reference}"));
        }

        Assert.True(
            offenders.Count == 0,
            $"A host is a composition root and must be referenced by nothing. Found: {Describe(offenders)}");
    }

    [Fact]
    public void Edge_Projects_Should_Not_Reference_Each_Other()
    {
        var offenders = new List<string>();

        foreach (var project in ProductionProjects().Where(IsEdge))
        {
            offenders.AddRange(graph.ReferencesOf(project)
                .Where(IsEdge)
                .Select(reference => $"{project} -> {reference}"));
        }

        Assert.True(
            offenders.Count == 0,
            "Edge projects never reference each other; shared code belongs below them. "
            + $"Found: {Describe(offenders)}");
    }

    [Fact]
    public void Implementations_Should_Not_Reference_Edge_Projects()
    {
        var offenders = new List<string>();

        foreach (var project in ProductionProjects().Where(IsImplementation))
        {
            offenders.AddRange(graph.ReferencesOf(project)
                .Where(reference => IsEdge(reference) || IsHost(reference))
                .Select(reference => $"{project} -> {reference}"));
        }

        Assert.True(
            offenders.Count == 0,
            $"An implementation must not depend on an edge or a host. Found: {Describe(offenders)}");
    }

    private static IEnumerable<string> ProductionProjects()
    {
        return graph.Projects.Where(project => !project.EndsWith(".Tests", StringComparison.Ordinal));
    }

    private static bool IsHost(string project)
    {
        return project.StartsWith("Dami.Host", StringComparison.Ordinal);
    }

    private static bool IsEdge(string project)
    {
        return project.StartsWith("Dami.Gateway", StringComparison.Ordinal);
    }

    private static bool IsImplementation(string project)
    {
        return project.StartsWith("Dami.", StringComparison.Ordinal)
            && project != CONTRACTS
            && project != CORE
            && !IsHost(project)
            && !IsEdge(project);
    }

    private static string Describe(IEnumerable<string> offenders)
    {
        var listed = string.Join(", ", offenders);
        return listed.Length == 0 ? "(none)" : listed;
    }
}
