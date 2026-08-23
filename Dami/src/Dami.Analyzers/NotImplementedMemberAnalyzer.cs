using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dami.Analyzers;

/// <summary>An interface implementation must not throw <c>NotImplementedException</c> (§5).</summary>
/// <remarks>
/// LSP: an implementation honours the whole contract or does not claim it. A member that
/// throws is a runtime trap for every caller holding the interface.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NotImplementedMemberAnalyzer : DiagnosticAnalyzer
{
    private const string NOT_IMPLEMENTED = "NotImplementedException";

    private static readonly DiagnosticDescriptor rule = new(
        DiagnosticIds.NOT_IMPLEMENTED_MEMBER,
        "Interface member throws NotImplementedException",
        "'{0}' implements an interface member but throws NotImplementedException",
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

        if (!ThrowsNotImplemented(method))
        {
            return;
        }

        var symbol = context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken);

        if (symbol is not null && ImplementsAnInterface(symbol))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                rule, method.Identifier.GetLocation(), symbol.Name));
        }
    }

    private static bool ThrowsNotImplemented(MethodDeclarationSyntax method)
    {
        return method.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Any(creation => creation.Type.ToString().EndsWith(NOT_IMPLEMENTED, System.StringComparison.Ordinal));
    }

    private static bool ImplementsAnInterface(IMethodSymbol symbol)
    {
        return symbol.ContainingType.AllInterfaces
            .SelectMany(contract => contract.GetMembers().OfType<IMethodSymbol>())
            .Any(member => SymbolEqualityComparer.Default.Equals(
                symbol.ContainingType.FindImplementationForInterfaceMember(member), symbol));
    }
}
