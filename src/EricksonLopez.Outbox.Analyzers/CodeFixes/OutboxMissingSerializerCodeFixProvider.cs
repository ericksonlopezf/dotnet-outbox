// Copyright © Erickson Lopez. MIT License.
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EricksonLopez.Outbox.Analyzers;

/// <summary>
/// Provides a code fix provider for OUTBOX005 that configures a serializer in the OutboxOptions setup.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OutboxMissingSerializerCodeFixProvider)), Shared]
public sealed class OutboxMissingSerializerCodeFixProvider : CodeFixProvider
{
    private const string Title = "Add .UseNativeAotJsonSerializer()";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(OutboxMessageAnalyzer.SerializationConfigRule.Id);

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = (await context.Document.GetSyntaxRootAsync(context.CancellationToken))!;

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var invocation = root.FindToken(diagnosticSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();

        if (invocation is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: ct => AddSerializerConfigAsync(context.Document, invocation, ct),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> AddSerializerConfigAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken ct)
    {
        var root = (await document.GetSyntaxRootAsync(ct))!;

        var args = invocation.ArgumentList.Arguments;
        if (args.Count == 0) return document;

        var lambdaArg = args[0].Expression;
        
        string paramName = "opts";
        BlockSyntax? newBlock = null;

        var serializerInvocation = SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(paramName),
                    SyntaxFactory.IdentifierName("UseNativeAotJsonSerializer")
                )
            )
        );

        LambdaExpressionSyntax? newLambda = null;

        if (lambdaArg is SimpleLambdaExpressionSyntax simpleLambda)
        {
            paramName = simpleLambda.Parameter.Identifier.Text;
            serializerInvocation = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName(paramName),
                        SyntaxFactory.IdentifierName("UseNativeAotJsonSerializer")
                    )
                )
            );
            
            if (simpleLambda.Block != null)
            {
                newBlock = simpleLambda.Block.AddStatements(serializerInvocation);
                newLambda = simpleLambda.WithBlock(newBlock);
            }
            else if (simpleLambda.ExpressionBody != null)
            {
                newBlock = SyntaxFactory.Block(
                    SyntaxFactory.ExpressionStatement(simpleLambda.ExpressionBody),
                    serializerInvocation
                );
                newLambda = simpleLambda.WithExpressionBody(null).WithBlock(newBlock);
            }
        }
        else if (lambdaArg is ParenthesizedLambdaExpressionSyntax parenLambda)
        {
            if (parenLambda.ParameterList.Parameters.Count > 0)
            {
                paramName = parenLambda.ParameterList.Parameters[0].Identifier.Text;
                serializerInvocation = SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName(paramName),
                            SyntaxFactory.IdentifierName("UseNativeAotJsonSerializer")
                        )
                    )
                );
            }
            
            if (parenLambda.Block != null)
            {
                newBlock = parenLambda.Block.AddStatements(serializerInvocation);
                newLambda = parenLambda.WithBlock(newBlock);
            }
            else if (parenLambda.ExpressionBody != null)
            {
                newBlock = SyntaxFactory.Block(
                    SyntaxFactory.ExpressionStatement(parenLambda.ExpressionBody),
                    serializerInvocation
                );
                newLambda = parenLambda.WithExpressionBody(null).WithBlock(newBlock);
            }
        }

        if (newLambda != null)
        {
            var newRoot = root.ReplaceNode(lambdaArg, newLambda);
            return document.WithSyntaxRoot(newRoot);
        }

        return document;
    }
}
