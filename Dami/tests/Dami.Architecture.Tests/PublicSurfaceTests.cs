using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Dami.Architecture.Tests;

/// <summary>
/// Guards against leaky abstractions on the public surface of the abstraction layers.
/// </summary>
/// <remarks>
/// docs/csharpcodestandards.md §6: if swapping the implementation forces a change to the
/// interface, the abstraction leaked. The cheapest mechanical proxy for that is whether
/// the mechanism's own types appear in the signature.
/// </remarks>
public sealed class PublicSurfaceTests
{
    /// <summary>Type names that name a mechanism rather than a capability.</summary>
    private static readonly string[] mechanismTypes =
    [
        "Npgsql",
        "Microsoft.EntityFrameworkCore",
        "System.Data.Common",
        "System.Data.IDb",
        "System.Net.Http.HttpClient",
        "System.Net.Http.HttpRequestMessage",
        "System.Net.Http.HttpResponseMessage",
        "System.Net.Sockets",
        "System.Linq.IQueryable",
        "System.Text.Json.JsonDocument",
        "System.Text.Json.Utf8Json",
    ];

    /// <summary>The layers that declare abstractions and must not name mechanisms.</summary>
    private static readonly string[] abstractionAssemblies = ["Dami.Contracts", "Dami.Core"];

    [Fact]
    public void Abstraction_Layers_Should_Not_Expose_Mechanism_Types()
    {
        var offenders = new List<string>();

        foreach (var assembly in AssemblyProbe.Load(abstractionAssemblies))
        {
            offenders.AddRange(LeaksIn(assembly));
        }

        Assert.True(
            offenders.Count == 0,
            "An abstraction must not name the mechanism behind it. Move the type behind the "
            + $"boundary or express the capability without it. Found: {Describe(offenders)}");
    }

    [Fact]
    public void Contracts_Should_Not_Reference_Any_Other_Dami_Assembly()
    {
        var offenders = AssemblyProbe.Load(["Dami.Contracts"])
            .SelectMany(assembly => assembly.GetReferencedAssemblies())
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("Dami", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Dami.Contracts must stand alone at the bottom. Found: {Describe(offenders)}");
    }

    private static IEnumerable<string> LeaksIn(Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            foreach (var member in PublicSignatureTypes(type))
            {
                var leaked = mechanismTypes.FirstOrDefault(banned =>
                    (member.Type.FullName ?? string.Empty).StartsWith(banned, StringComparison.Ordinal));

                if (leaked is not null)
                {
                    yield return $"{type.Name}.{member.Member} exposes {leaked}";
                }
            }
        }
    }

    private static IEnumerable<(string Member, Type Type)> PublicSignatureTypes(Type type)
    {
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            yield return (method.Name, method.ReturnType);

            foreach (var parameter in method.GetParameters())
            {
                yield return (method.Name, parameter.ParameterType);
            }
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            yield return (property.Name, property.PropertyType);
        }
    }

    private static string Describe(IEnumerable<string> offenders)
    {
        var listed = string.Join("; ", offenders);
        return listed.Length == 0 ? "(none)" : listed;
    }
}
