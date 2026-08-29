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
/// Provides a code fix provider for OUTBOX003 that adds the missing <c>[InboxConsumer]</c> attribute to consumers.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OutboxMissingInboxConsumerCodeFixProvider)), Shared]
public sealed class OutboxMissingInboxConsumerCodeFixProvider : CodeFixProvider
{
    private const string Title = "Add [InboxConsumer] attribute";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(OutboxMessageAnalyzer.NonIdempotentConsumerRule.Id);

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = (await context.Document.GetSyntaxRootAsync(context.CancellationToken))!;

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var typeDecl = root.FindToken(diagnosticSpan.Start)
            .Parent?
            .AncestorsAndSelf()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault();

        if (typeDecl is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: ct => AddInboxConsumerAttributeAsync(context.Document, typeDecl, ct),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> AddInboxConsumerAttributeAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        CancellationToken ct)
    {
        var root = (await document.GetSyntaxRootAsync(ct))!;

        var attribute = SyntaxFactory.Attribute(
            SyntaxFactory.ParseName("EricksonLopez.Outbox.Contracts.InboxConsumer"));

        var attributeList = SyntaxFactory.AttributeList(
            SyntaxFactory.SeparatedList(new[] { attribute }));

        var newTypeDecl = typeDecl.AddAttributeLists(attributeList);
        var newRoot = root.ReplaceNode(typeDecl, newTypeDecl);
        return document.WithSyntaxRoot(newRoot);
    }
}
