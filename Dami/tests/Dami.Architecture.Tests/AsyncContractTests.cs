using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Xunit;

namespace Dami.Architecture.Tests;

/// <summary>
/// "Async at the core" expressed as checks rather than intent.
/// </summary>
/// <remarks>
/// The banned-API analyzer stops a caller blocking on a task. It cannot say whether the
/// contract was async-shaped in the first place, which is what these cover: the Async
/// suffix from §1, and C-06's requirement that cancellation is threaded through
/// everything.
/// </remarks>
public sealed class AsyncContractTests
{
    private static readonly string[] productionAssemblies =
        ["Dami.Contracts", "Dami.Core", "Dami.Transport"];

    [Fact]
    public void Awaitable_Returning_Methods_Should_End_With_Async()
    {
        var offenders = PublicMethods()
            .Where(entry => IsAwaitable(entry.Method.ReturnType))
            .Where(entry => !ImplementsExternalContract(entry.Type, entry.Method))
            .Where(entry => !entry.Method.Name.EndsWith("Async", StringComparison.Ordinal))
            .Select(entry => $"{entry.Type.Name}.{entry.Method.Name}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"A method returning an awaitable carries the Async suffix. Found: {Describe(offenders)}");
    }

    [Fact]
    public void Awaitable_Returning_Methods_Should_Accept_A_CancellationToken()
    {
        var offenders = PublicMethods()
            .Where(entry => IsAwaitable(entry.Method.ReturnType))
            .Where(entry => !ImplementsExternalContract(entry.Type, entry.Method))
            .Where(entry => !AcceptsCancellation(entry.Method))
            .Select(entry => $"{entry.Type.Name}.{entry.Method.Name}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "C-06: cancellation is threaded through everything, including proactive work. "
            + $"Found: {Describe(offenders)}");
    }

    [Fact]
    public void No_Public_Method_Should_Return_Bare_Void_Asynchronously()
    {
        var offenders = PublicMethods()
            .Where(entry => entry.Method.GetCustomAttributes()
                .Any(attribute => attribute.GetType().Name == "AsyncStateMachineAttribute"))
            .Where(entry => entry.Method.ReturnType == typeof(void))
            .Select(entry => $"{entry.Type.Name}.{entry.Method.Name}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"async void cannot be awaited and its exceptions escape. Found: {Describe(offenders)}");
    }

    private static IEnumerable<(Type Type, MethodInfo Method)> PublicMethods()
    {
        foreach (var assembly in AssemblyProbe.Load(productionAssemblies))
        {
            foreach (var entry in MethodsIn(assembly))
            {
                yield return entry;
            }
        }
    }

    private static IEnumerable<(Type Type, MethodInfo Method)> MethodsIn(Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            foreach (var method in DeclaredMethodsOf(type))
            {
                yield return (type, method);
            }
        }
    }

    private static IEnumerable<MethodInfo> DeclaredMethodsOf(Type type)
    {
        return type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName);
    }

    private static bool IsAwaitable(Type returnType)
    {
        var name = returnType.FullName ?? returnType.Name;

        return name.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal)
            || name.StartsWith("System.Threading.Tasks.ValueTask", StringComparison.Ordinal)
            || name.StartsWith("System.Collections.Generic.IAsyncEnumerable", StringComparison.Ordinal);
    }

    /// <summary>
    /// True when the method implements an interface declared outside Dami.
    /// </summary>
    /// <remarks>
    /// These signatures are not ours to change. <c>IAsyncDisposable.DisposeAsync()</c>
    /// takes no cancellation token by design, and demanding one would be a rule the
    /// framework forbids obeying. C-06 governs the contracts we author.
    /// </remarks>
    private static bool ImplementsExternalContract(Type type, MethodInfo method)
    {
        if (type.IsInterface)
        {
            return false;
        }

        foreach (var contract in type.GetInterfaces())
        {
            var owner = contract.Assembly.GetName().Name ?? string.Empty;
            if (owner.StartsWith("Dami", StringComparison.Ordinal))
            {
                continue;
            }

            if (type.GetInterfaceMap(contract).TargetMethods.Contains(method))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AcceptsCancellation(MethodBase method)
    {
        return method.GetParameters().Any(parameter => parameter.ParameterType == typeof(CancellationToken));
    }

    private static string Describe(IEnumerable<string> offenders)
    {
        var listed = string.Join(", ", offenders);
        return listed.Length == 0 ? "(none)" : listed;
    }
}
