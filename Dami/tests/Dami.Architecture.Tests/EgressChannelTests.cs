using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Dami.Architecture.Tests;

/// <summary>
/// ADR-0024: holding an egress channel must be as visible as holding an egress client.
/// </summary>
/// <remarks>
/// D-012's guarantee is that the set of things able to reach off the host is knowable by
/// reading the composition root. A second mechanism is a second way to lose that, so the
/// holders are pinned here: adding one fails this test until someone writes it down,
/// which is the whole point. The list is a decision record, not a convenience.
/// </remarks>
public sealed class EgressChannelTests
{
    /// <summary>Every production assembly a channel could plausibly be held in.</summary>
    private static readonly string[] productionAssemblies =
    [
        "Dami.Contracts",
        "Dami.Core",
        "Dami.Persistence",
        "Dami.Privacy",
        "Dami.Providers",
        "Dami.Proactive",
        "Dami.Capabilities",
        "Dami.Capabilities.Native",
        "Dami.Capabilities.Mcp",
        "Dami.Capabilities.Sandboxed",
        "Dami.Capabilities.Skills",
        "Dami.Vision",
        "Dami.Transport",
        "Dami.Authentication",
        "Dami.Gateway.Discord",
        "Dami.Host.Discord",
    ];

    /// <summary>
    /// The types allowed to implement or take an <c>IEgressChannel</c>. Every addition is
    /// a decision about what may reach off the host.
    /// </summary>
    private static readonly string[] permittedHolders =
    [
        "Dami.Gateway.Discord.DiscordEgressChannel",
        "Dami.Host.Discord.DiscordGatewayWorker",
    ];

    [Fact]
    public void Only_Declared_Holders_Should_Touch_An_Egress_Channel()
    {
        var undeclared = HoldersOf("IEgressChannel")
            .Except(permittedHolders, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            undeclared.Count == 0,
            "These types implement or take an IEgressChannel but are not declared holders "
            + "(ADR-0024). If the dependency is intended, add it to permittedHolders and say "
            + $"why in the commit: {string.Join(", ", undeclared)}");
    }

    [Fact]
    public void The_Probe_Should_Actually_See_The_Discord_Channel()
    {
        // Guards the rules above from passing vacuously. AssemblyProbe skips assemblies it
        // cannot load, so a rule over an assembly that is absent from the test output is a
        // rule over nothing - and it would look green forever.
        Assert.Contains(
            "Dami.Gateway.Discord.DiscordEgressChannel",
            HoldersOf("IEgressChannel"),
            StringComparer.Ordinal);
    }

    [Fact]
    public void The_Local_Only_Tier_Should_Not_Hold_An_Egress_Channel()
    {
        // The proactive tier reasons over the corpus. A channel there would put the
        // profile one refactor away from a third party.
        var holders = HoldersOf("IEgressChannel")
            .Where(type => type.StartsWith("Dami.Proactive.", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            holders.Count == 0,
            $"The proactive tier must not hold an egress channel. Found: {string.Join(", ", holders)}");
    }

    /// <summary>Types that implement the named interface or take one as a dependency.</summary>
    private static List<string> HoldersOf(string interfaceName)
    {
        var holders = new List<string>();

        foreach (var assembly in AssemblyProbe.Load(productionAssemblies))
        {
            holders.AddRange(
                Types(assembly)
                    .Where(type => !type.IsInterface)
                    .Where(type => Implements(type, interfaceName) || TakesParameter(type, interfaceName))
                    .Select(type => type.FullName ?? type.Name));
        }

        return holders;
    }

    private static bool Implements(Type type, string interfaceName) =>
        type.GetInterfaces().Any(
            contract => string.Equals(contract.Name, interfaceName, StringComparison.Ordinal));

    private static IEnumerable<Type> Types(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException loaded)
        {
            return loaded.Types.Where(type => type is not null)!;
        }
    }

    private static bool TakesParameter(Type type, string interfaceName) =>
        type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Any(parameter => string.Equals(
                parameter.ParameterType.Name, interfaceName, StringComparison.Ordinal));
}
