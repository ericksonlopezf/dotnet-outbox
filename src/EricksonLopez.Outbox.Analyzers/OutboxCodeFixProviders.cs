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
/// Code Fix Provider for OUTBOX001:
/// Adds a missing <c>Guid Id</c> property to message types.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OutboxMissingIdCodeFixProvider)), Shared]
public sealed class OutboxMissingIdCodeFixProvider : CodeFixProvider
{
    private const string Title = "Add 'Guid Id' property";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(OutboxMessageAnalyzer.MissingIdRule.Id);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
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
        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null) return document;

        var idProperty = SyntaxFactory.ParseMemberDeclaration("public System.Guid Id { get; } = System.Guid.NewGuid();\r\n")!
            .WithLeadingTrivia(SyntaxFactory.Whitespace("    "))
            .WithTrailingTrivia(SyntaxFactory.EndOfLine("\r\n"));

        var newTypeDecl = typeDecl.WithMembers(typeDecl.Members.Insert(0, idProperty));
        var newRoot = root.ReplaceNode(typeDecl, newTypeDecl);
        return document.WithSyntaxRoot(newRoot);
    }
}

/// <summary>
/// Code Fix Provider for OUTBOX002:
/// Generates a stable <c>[OutboxMessage("...")]</c> type alias from the class name.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OutboxMissingAliasCodeFixProvider)), Shared]
public sealed class OutboxMissingAliasCodeFixProvider : CodeFixProvider
{
    private const string Title = "Add stable [OutboxMessage] alias";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(OutboxMessageAnalyzer.MissingAliasRule.Id);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
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
        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null) return document;

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

    private static string ToKebabCase(string input)
    {
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            if (i > 0 && char.IsUpper(input[i]))
                result.Append('.');
            result.Append(char.ToLowerInvariant(input[i]));
        }
        return result.ToString();
    }
}

/// <summary>
/// Code Fix Provider for OUTBOX007:
/// Replaces a null transaction argument with a placeholder identifier.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OutboxNullTransactionCodeFixProvider)), Shared]
public sealed class OutboxNullTransactionCodeFixProvider : CodeFixProvider
{
    private const string Title = "Provide a transaction context";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(OutboxMessageAnalyzer.NullTransactionRule.Id);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
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
                createChangedDocument: ct => ReplaceNullWithPlaceholderAsync(context.Document, argument, ct),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> ReplaceNullWithPlaceholderAsync(
        Document document,
        ArgumentSyntax argument,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null) return document;

        var placeholder = SyntaxFactory.IdentifierName("transactionContext")
            .WithTrailingTrivia(SyntaxFactory.Comment("/* TODO: Provide IOutboxTransactionContext */ "));

        var newArgument = argument.WithExpression(placeholder);
        var newRoot = root.ReplaceNode(argument, newArgument);
        return document.WithSyntaxRoot(newRoot);
    }
}

/// <summary>
/// Code Fix Provider for OUTBOX003:
/// Adds the missing <c>[InboxConsumer]</c> attribute to consumers.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OutboxMissingInboxConsumerCodeFixProvider)), Shared]
public sealed class OutboxMissingInboxConsumerCodeFixProvider : CodeFixProvider
{
    private const string Title = "Add [InboxConsumer] attribute";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(OutboxMessageAnalyzer.NonIdempotentConsumerRule.Id);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
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
        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null) return document;

        var attribute = SyntaxFactory.Attribute(
            SyntaxFactory.ParseName("EricksonLopez.Outbox.Contracts.InboxConsumer"));

        var attributeList = SyntaxFactory.AttributeList(
            SyntaxFactory.SeparatedList(new[] { attribute }));

        var newTypeDecl = typeDecl.AddAttributeLists(attributeList);
        var newRoot = root.ReplaceNode(typeDecl, newTypeDecl);
        return document.WithSyntaxRoot(newRoot);
    }
}

/// <summary>
/// Code Fix Provider for OUTBOX004:
/// Changes the infinite or invalid max attempts to a safe default of 3.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OutboxInfiniteRetriesCodeFixProvider)), Shared]
public sealed class OutboxInfiniteRetriesCodeFixProvider : CodeFixProvider
{
    private const string Title = "Set MaxAttempts to 3";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(OutboxMessageAnalyzer.InfiniteRetriesRule.Id);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
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
        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null) return document;

        var literal = SyntaxFactory.LiteralExpression(
            SyntaxKind.NumericLiteralExpression,
            SyntaxFactory.Literal(3));

        var newArgument = argument.WithExpression(literal);
        var newRoot = root.ReplaceNode(argument, newArgument);
        return document.WithSyntaxRoot(newRoot);
    }
}

/// <summary>
/// Code Fix Provider for OUTBOX006:
/// Generates a stable <c>[OutboxMessage("...")]</c> type alias for integration events.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OutboxIntegrationEventAliasCodeFixProvider)), Shared]
public sealed class OutboxIntegrationEventAliasCodeFixProvider : CodeFixProvider
{
    private const string Title = "Add stable [OutboxMessage] alias";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(OutboxMessageAnalyzer.MissingOutboxMessageAttributeRule.Id);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
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
        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null) return document;

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

    private static string ToKebabCase(string input)
    {
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            if (i > 0 && char.IsUpper(input[i]))
                result.Append('.');
            result.Append(char.ToLowerInvariant(input[i]));
        }
        return result.ToString();
    }
}

/// <summary>
/// Code Fix Provider for OUTBOX005:
/// Adds a call to UseGeneratedTypes() / UseNativeAotJsonSerializer() in the OutboxOptions configuration.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OutboxMissingSerializerCodeFixProvider)), Shared]
public sealed class OutboxMissingSerializerCodeFixProvider : CodeFixProvider
{
    private const string Title = "Add .UseNativeAotJsonSerializer()";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(OutboxMessageAnalyzer.SerializationConfigRule.Id);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
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
        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null) return document;

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
