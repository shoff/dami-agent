using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dami.Analyzers;

/// <summary>Loops nest at most two deep (§3).</summary>
/// <remarks>
/// Reported on the innermost offending loop, which is the one to extract.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LoopNestingAnalyzer : DiagnosticAnalyzer
{
    private const int MAX_DEPTH = 2;

    private static readonly SyntaxKind[] loopKinds =
    [
        SyntaxKind.ForStatement,
        SyntaxKind.ForEachStatement,
        SyntaxKind.ForEachVariableStatement,
        SyntaxKind.WhileStatement,
        SyntaxKind.DoStatement,
    ];

    private static readonly DiagnosticDescriptor rule = new(
        DiagnosticIds.LOOP_NESTING_TOO_DEEP,
        "Loop nesting is too deep",
        "Loop nesting is {0} levels; the limit is {1}. Extract the inner logic to a private method.",
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
        context.RegisterSyntaxNodeAction(Report, loopKinds);
    }

    private static void Report(SyntaxNodeAnalysisContext context)
    {
        if (HasNestedLoop(context.Node))
        {
            return;
        }

        var depth = 1 + context.Node.Ancestors().Count(IsLoop);

        if (depth > MAX_DEPTH)
        {
            context.ReportDiagnostic(Diagnostic.Create(rule, context.Node.GetFirstToken().GetLocation(), depth, MAX_DEPTH));
        }
    }

    private static bool HasNestedLoop(SyntaxNode node)
    {
        return node.DescendantNodes().Any(IsLoop);
    }

    private static bool IsLoop(SyntaxNode node)
    {
        return loopKinds.Contains(node.Kind());
    }
}
