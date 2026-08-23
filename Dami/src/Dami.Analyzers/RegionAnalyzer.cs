using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dami.Analyzers;

/// <summary>Bans <c>#region</c> (§3).</summary>
/// <remarks>
/// A region is almost always a type asking to be split. Folding the evidence away does
/// not reduce the complexity, it hides it.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RegionAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor rule = new(
        DiagnosticIds.NO_REGIONS,
        "Do not use #region",
        "#region is banned; split the type instead",
        DiagnosticIds.CATEGORY,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Report, SyntaxKind.RegionDirectiveTrivia);
    }

    private static void Report(SyntaxNodeAnalysisContext context)
    {
        context.ReportDiagnostic(Diagnostic.Create(rule, context.Node.GetLocation()));
    }
}
