using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Dami.Analyzers;

/// <summary>Constructor dependencies are required, never optional (§5).</summary>
/// <remarks>
/// A container resolves the default silently and the feature is disabled with no error.
///
/// Two exclusions keep this a rule about dependencies rather than about optional
/// parameters in general. Only abstraction-typed parameters are flagged, because an
/// optional <c>int retries = 3</c> is a value. And records are skipped entirely: C-03
/// makes records data and classes services, so a container never constructs one, and
/// §4 requires collections be exposed as <c>IReadOnlyList&lt;T&gt;</c> and friends -
/// which would otherwise make every optional collection on a model a false positive.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OptionalConstructorParameterAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor rule = new(
        DiagnosticIds.OPTIONAL_CONSTRUCTOR_PARAMETER,
        "Constructor dependency is optional",
        "Parameter '{0}' is an optional dependency; a container resolves the default and silently disables the feature",
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
        context.RegisterSyntaxNodeAction(Report, SyntaxKind.ConstructorDeclaration);
    }

    private static void Report(SyntaxNodeAnalysisContext context)
    {
        var constructor = (ConstructorDeclarationSyntax)context.Node;

        if (constructor.Parent is RecordDeclarationSyntax)
        {
            return;
        }

        foreach (var parameter in constructor.ParameterList.Parameters)
        {
            if (parameter.Default is null || parameter.Type is null)
            {
                continue;
            }

            if (IsAbstraction(context, parameter.Type))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    rule, parameter.GetLocation(), parameter.Identifier.ValueText));
            }
        }
    }

    private static bool IsAbstraction(SyntaxNodeAnalysisContext context, TypeSyntax type)
    {
        var resolved = context.SemanticModel.GetTypeInfo(type, context.CancellationToken).Type;

        if (resolved is null)
        {
            return false;
        }

        if (resolved is INamedTypeSymbol named && named.IsGenericType
            && named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
        {
            return false;
        }

        return resolved.TypeKind == TypeKind.Interface
            || (resolved.TypeKind == TypeKind.Class && resolved.IsAbstract)
            || resolved.TypeKind == TypeKind.Delegate;
    }
}
