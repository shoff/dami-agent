using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dami.Analyzers;

/// <summary>Bans <c>dynamic</c> (§5).</summary>
/// <remarks>
/// dynamic moves every binding error from build time to run time, which is precisely the
/// trade this codebase refuses.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DynamicTypeAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor rule = new(
        DiagnosticIds.NO_DYNAMIC,
        "Do not use dynamic",
        "'dynamic' is banned; use a concrete type or an interface",
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
        context.RegisterSyntaxNodeAction(Report, SyntaxKind.IdentifierName);
    }

    private static void Report(SyntaxNodeAnalysisContext context)
    {
        var identifier = (Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax)context.Node;

        if (!identifier.Identifier.ValueText.Equals("dynamic", System.StringComparison.Ordinal))
        {
            return;
        }

        if (context.SemanticModel.GetTypeInfo(identifier, context.CancellationToken).Type?.TypeKind == TypeKind.Dynamic)
        {
            context.ReportDiagnostic(Diagnostic.Create(rule, identifier.GetLocation()));
        }
    }
}
