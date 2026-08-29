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
/// Provides a code fix provider for OUTBOX001 that adds a missing <c>Guid Id</c> property to message types.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OutboxMissingIdCodeFixProvider)), Shared]
public sealed class OutboxMissingIdCodeFixProvider : CodeFixProvider
{
    private const string Title = "Add 'Guid Id' property";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(OutboxMessageAnalyzer.MissingIdRule.Id);

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
                createChangedDocument: ct => AddGuidIdPropertyAsync(context.Document, typeDecl, ct),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> AddGuidIdPropertyAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        CancellationToken ct)
    {
        var root = (await document.GetSyntaxRootAsync(ct))!;

        var idProperty = SyntaxFactory.ParseMemberDeclaration("    public Guid Id { get; } = Guid.NewGuid();\r\n")!;

        var newTypeDecl = typeDecl.WithMembers(typeDecl.Members.Insert(0, idProperty));
        var newRoot = root.ReplaceNode(typeDecl, newTypeDecl);
        return document.WithSyntaxRoot(newRoot);
    }
}
