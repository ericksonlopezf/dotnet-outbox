// Copyright © Erickson Lopez. MIT License.
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EricksonLopez.Outbox.Analyzers;

/// <summary>
/// Provides a code fix provider for OUTBOX002 that generates a stable <c>[OutboxMessage]</c> attribute from the class name.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OutboxMissingAliasCodeFixProvider)), Shared]
public sealed class OutboxMissingAliasCodeFixProvider : CodeFixProvider
{
    private const string Title = "Add stable [OutboxMessage] alias";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(OutboxMessageAnalyzer.MissingAliasRule.Id);

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
                createChangedDocument: ct => AddOutboxMessageAttributeAsync(context.Document, typeDecl, ct),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> AddOutboxMessageAttributeAsync(
        Document document,
        TypeDeclarationSyntax typeDecl,
        CancellationToken ct)
    {
        var root = (await document.GetSyntaxRootAsync(ct))!;

        var typeName = typeDecl.Identifier.Text;
        var alias = ToKebabCase(typeName);

        var attribute = SyntaxFactory.Attribute(
            SyntaxFactory.ParseName("EricksonLopez.Outbox.Contracts.OutboxMessage"),
            SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SeparatedList(new[]
                {
                    SyntaxFactory.AttributeArgument(
                        SyntaxFactory.LiteralExpression(
                            SyntaxKind.StringLiteralExpression,
                            SyntaxFactory.Literal(alias)))
                })));

        var attributeList = SyntaxFactory.AttributeList(
            SyntaxFactory.SeparatedList(new[] { attribute }));

        var newTypeDecl = typeDecl.AddAttributeLists(attributeList);
        var newRoot = root.ReplaceNode(typeDecl, newTypeDecl);
        return document.WithSyntaxRoot(newRoot);
    }

    internal static string ToKebabCase(string input)
    {
        var result = new StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            if (i > 0 && char.IsUpper(input[i]))
                result.Append('.');
            result.Append(char.ToLowerInvariant(input[i]));
        }
        return result.ToString();
    }
}
