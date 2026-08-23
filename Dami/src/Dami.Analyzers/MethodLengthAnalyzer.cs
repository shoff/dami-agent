using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dami.Analyzers;

/// <summary>Methods are 30 lines or fewer (§3).</summary>
/// <remarks>
/// The limit counts the statement lines of the body, not the signature or braces, so a
/// long parameter list is not punished twice.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MethodLengthAnalyzer : DiagnosticAnalyzer
{
    private const int MAX_BODY_LINES = 30;

    private static readonly DiagnosticDescriptor rule = new(
        DiagnosticIds.METHOD_TOO_LONG,
        "Method is too long",
        "'{0}' has {1} body lines; the limit is {2}",
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
        context.RegisterSyntaxNodeAction(Report, SyntaxKind.MethodDeclaration);
    }

    private static void Report(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;

        if (method.Body is null || method.Body.Statements.Count == 0)
        {
            return;
        }

        var span = method.Body.Statements.Span;
        var lines = method.SyntaxTree.GetLineSpan(span, context.CancellationToken);
        var count = lines.EndLinePosition.Line - lines.StartLinePosition.Line + 1;

        if (count > MAX_BODY_LINES)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                rule, method.Identifier.GetLocation(), method.Identifier.ValueText, count, MAX_BODY_LINES));
        }
    }
}
