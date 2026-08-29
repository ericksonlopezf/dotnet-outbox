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
/// Provides a code fix provider for OUTBOX004 that replaces non-positive or excessive retry attempts with a default count.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OutboxInfiniteRetriesCodeFixProvider)), Shared]
public sealed class OutboxInfiniteRetriesCodeFixProvider : CodeFixProvider
{
    private const string Title = "Set MaxAttempts to 3";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(OutboxMessageAnalyzer.InfiniteRetriesRule.Id);

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = (await context.Document.GetSyntaxRootAsync(context.CancellationToken))!;

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var argument = root.FindToken(diagnosticSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<ArgumentSyntax>()
            .FirstOrDefault();

        if (argument is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: ct => SetMaxAttemptsToDefaultAsync(context.Document, argument, ct),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> SetMaxAttemptsToDefaultAsync(
        Document document,
        ArgumentSyntax argument,
        CancellationToken ct)
    {
        var root = (await document.GetSyntaxRootAsync(ct))!;

        var literal = SyntaxFactory.LiteralExpression(
            SyntaxKind.NumericLiteralExpression,
            SyntaxFactory.Literal(3));

        var newArgument = argument.WithExpression(literal);
        var newRoot = root.ReplaceNode(argument, newArgument);
        return document.WithSyntaxRoot(newRoot);
    }
}
