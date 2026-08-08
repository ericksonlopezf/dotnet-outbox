using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EricksonLopez.Outbox.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class TransactionRequiredAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "OUTBOX010";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "StoreAsync called without a transaction in the fluent builder",
        messageFormat: "The outbox message is being saved without a transaction. You must call .WithTransaction(...) before .StoreAsync().",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The Transactional Outbox pattern requires messages to be saved within the same database transaction as the business data. Calling StoreAsync() on the builder without first calling WithTransaction() will throw at runtime.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeStoreAsyncCall, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeStoreAsyncCall(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (memberAccess.Name.Identifier.Text != "StoreAsync")
            return;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        // Ensure it's the OutboxMessageBuilder.StoreAsync method
        bool isBuilderStoreAsync = methodSymbol.ContainingType.Name == "OutboxMessageBuilder" &&
                                   methodSymbol.ContainingType.ContainingNamespace?.ToDisplayString().StartsWith("EricksonLopez.Outbox", System.StringComparison.Ordinal) == true;

        if (!isBuilderStoreAsync)
            return;

        // Traverse the fluent chain to see if WithTransaction is called
        bool hasTransaction = false;
        ExpressionSyntax currentExpr = memberAccess.Expression;

        while (currentExpr != null)
        {
            if (currentExpr is InvocationExpressionSyntax chainInvocation)
            {
                if (chainInvocation.Expression is MemberAccessExpressionSyntax chainMemberAccess)
                {
                    if (chainMemberAccess.Name.Identifier.Text == "WithTransaction")
                    {
                        hasTransaction = true;
                        break;
                    }
                    currentExpr = chainMemberAccess.Expression;
                }
                else
                {
                    break;
                }
            }
            else if (currentExpr is MemberAccessExpressionSyntax plainMemberAccess)
            {
                currentExpr = plainMemberAccess.Expression;
            }
            else
            {
                break;
            }
        }

        if (!hasTransaction)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.Name.GetLocation()));
        }
    }
}
