using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dami.Analyzers.Tests;

/// <summary>Compiles a snippet in memory and runs one analyzer over it.</summary>
/// <remarks>
/// Deliberately hand-rolled rather than using the analyzer-testing packages: it is
/// roughly thirty lines, adds no dependency, and keeps what the test actually does
/// visible.
/// </remarks>
public static class AnalyzerHarness
{
    private static readonly ImmutableArray<MetadataReference> references = BuildReferences();

    /// <summary>Diagnostics that <paramref name="analyzer"/> reports for <paramref name="source"/>.</summary>
    public static async Task<ImmutableArray<Diagnostic>> RunAsync(
        DiagnosticAnalyzer analyzer,
        string source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(source);

        var compilation = CSharpCompilation.Create(
            "AnalyzerProbe",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The ids reported, in source order, for terse assertions.</summary>
    public static async Task<string[]> IdsAsync(
        DiagnosticAnalyzer analyzer,
        string source,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = await RunAsync(analyzer, source, cancellationToken).ConfigureAwait(false);
        return diagnostics.Select(diagnostic => diagnostic.Id).ToArray();
    }

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;

        return trusted
            .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }
}
