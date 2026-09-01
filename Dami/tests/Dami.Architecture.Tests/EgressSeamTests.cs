using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Dami.Architecture.Tests;

/// <summary>
/// Every seam that reaches off this host, pinned — and the rule that none of them may sit
/// in the same type as the profile.
/// </summary>
/// <remarks>
/// D-012 says enforcement is auditable in the composition root. Until this file existed
/// that was true of exactly one of five seams: <c>IEgressChannel</c> had
/// <see cref="EgressChannelTests"/> and <c>IEgressClient</c>, <c>IFrontierChat</c>,
/// <c>IImageGenerator</c> and <c>IDiscordRest</c> had nothing, so a new type could take
/// the door that carries profile-derived bodies without any test noticing.
///
/// The second rule here is the one the recall and weather services were designed around
/// and which was previously enforced only by a comment: a type that can transmit must not
/// also be able to read the health or fitness stores. That split is the whole privacy
/// argument of H12 and H14, and a convention is not an argument.
/// </remarks>
public sealed class EgressSeamTests
{
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

    /// <summary>Every interface through which bytes can leave this machine.</summary>
    private static readonly string[] egressSeams =
    [
        "IEgressClient",
        "IEgressChannel",
        "IFrontierChat",
        "IImageGenerator",
        "IDiscordRest",
    ];

    /// <summary>The stores that hold what D-012 protects.</summary>
    private static readonly string[] profileStores =
    [
        "IHealthEventStore",
        "IFitnessStore",
    ];

    /// <summary>
    /// The types allowed to take a frontier chat. Additions are decisions about what may
    /// send a body to a third-party model.
    /// </summary>
    private static readonly string[] permittedFrontierHolders =
    [
        "Dami.Core.Frontier.AugmentedFrontierTurn",
        "Dami.Core.Frontier.BriefExecutor",
        "Dami.Core.Frontier.FrontierTracedTurnRunner",
        "Dami.Core.TaskBoard.FrontierFeaturePlanner",
        "Dami.Providers.AnthropicChatClient",
        "Dami.Providers.CodexChatClient",
    ];

    /// <summary>The types allowed to generate images (ADR-0027). One door, one bill.</summary>
    private static readonly string[] permittedImageHolders =
    [
        "Dami.Providers.OpenAiImageGenerator",
        "Dami.Proactive.Portrait.DailyPortraitService",
    ];

    [Fact]
    public void No_Type_Should_Hold_An_Egress_Seam_And_A_Profile_Store()
    {
        // The H12/H14 split expressed as a rule rather than a comment: the half that can
        // transmit must not be able to read health or fitness, and the half that reads
        // them must not be able to transmit.
        var offenders = new List<string>();

        foreach (var assembly in AssemblyProbe.Load(productionAssemblies))
        {
            offenders.AddRange(
                Types(assembly)
                    .Where(type => !type.IsInterface)
                    .Where(type => egressSeams.Any(seam => Holds(type, seam)))
                    .Where(type => profileStores.Any(store => Holds(type, store)))
                    .Select(type => type.FullName ?? type.Name));
        }

        Assert.True(
            offenders.Count == 0,
            "A type that can transmit must not also read the profile (D-012). Found: "
                + string.Join(", ", offenders));
    }

    [Fact]
    public void Frontier_Chat_Holders_Should_Be_The_Pinned_Set()
    {
        var unexpected = HoldersOf("IFrontierChat")
            .Where(holder => !permittedFrontierHolders.Contains(holder, StringComparer.Ordinal))
            .ToList();

        Assert.True(
            unexpected.Count == 0,
            "New holders of the frontier door must be recorded, not inherited. Found: "
                + string.Join(", ", unexpected));
    }

    [Fact]
    public void Image_Generator_Holders_Should_Be_The_Pinned_Set()
    {
        var unexpected = HoldersOf("IImageGenerator")
            .Where(holder => !permittedImageHolders.Contains(holder, StringComparer.Ordinal))
            .ToList();

        Assert.True(
            unexpected.Count == 0,
            "Image generation costs money per call; new holders are a decision. Found: "
                + string.Join(", ", unexpected));
    }

    [Fact]
    public void The_Probe_Should_Actually_See_The_Assemblies()
    {
        // AssemblyProbe skips what it cannot load, which is how the architecture tests
        // were once "largely vacuous". A rule that inspects nothing passes forever.
        Assert.True(AssemblyProbe.Load(productionAssemblies).Count() >= productionAssemblies.Length - 1);
    }

    [Fact]
    public void The_Probe_Should_Actually_See_A_Known_Frontier_Holder()
    {
        // Proves the seam scan finds real types rather than returning an empty list.
        Assert.Contains("Dami.Providers.CodexChatClient", HoldersOf("IFrontierChat"));
    }

    private static bool Holds(Type type, string interfaceName) =>
        Implements(type, interfaceName) || TakesParameter(type, interfaceName);

    private static List<string> HoldersOf(string interfaceName)
    {
        var holders = new List<string>();

        foreach (var assembly in AssemblyProbe.Load(productionAssemblies))
        {
            holders.AddRange(
                Types(assembly)
                    .Where(type => !type.IsInterface)
                    .Where(type => Holds(type, interfaceName))
                    .Select(type => type.FullName ?? type.Name));
        }

        return holders;
    }

    private static bool Implements(Type type, string interfaceName) =>
        type.GetInterfaces().Any(
            contract => string.Equals(contract.Name, interfaceName, StringComparison.Ordinal));

    private static bool TakesParameter(Type type, string interfaceName) =>
        type.GetConstructors().Any(constructor => constructor.GetParameters().Any(
            parameter => string.Equals(
                parameter.ParameterType.Name, interfaceName, StringComparison.Ordinal)));

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
}
