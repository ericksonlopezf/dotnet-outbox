// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EricksonLopez.Outbox.Analyzers;

/// <summary>
/// Provides a Roslyn diagnostic analyzer that enforces Outbox design patterns and invariants at compile time.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OutboxMessageAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic identifier for missing message identifier properties.
    /// </summary>
    public const string MissingIdDiagnosticId = "OUTBOX001";

    /// <summary>
    /// The diagnostic rule descriptor for missing message identifier properties.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingIdRule = new(
        id: MissingIdDiagnosticId,
        title: "Message type missing 'Guid Id' property",
        messageFormat: "Type '{0}' is missing a public 'Guid Id' property required for outbox identification.",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Outbox messages must have an explicit 'Guid Id' property to guarantee unique delivery tracking.");

    /// <summary>
    /// The diagnostic identifier for missing message aliases.
    /// </summary>
    public const string MissingAliasDiagnosticId = "OUTBOX002";

    /// <summary>
    /// The diagnostic rule descriptor for missing message aliases.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingAliasRule = new(
        id: MissingAliasDiagnosticId,
        title: "Missing [OutboxMessage] attribute",
        messageFormat: "Type '{0}' is missing the [OutboxMessage(\"alias\")] attribute. NativeAOT serialization will fail at runtime.",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "All types stored via IOutbox<T>.StoreAsync must be decorated with [OutboxMessage(\"alias\")] to guarantee NativeAOT-safe, reflection-free serialization.");

    /// <summary>
    /// The diagnostic identifier for non-idempotent consumers.
    /// </summary>
    public const string NonIdempotentConsumerDiagnosticId = "OUTBOX003";

    /// <summary>
    /// The diagnostic rule descriptor for non-idempotent consumers.
    /// </summary>
    public static readonly DiagnosticDescriptor NonIdempotentConsumerRule = new(
        id: NonIdempotentConsumerDiagnosticId,
        title: "Consumer is not idempotent",
        messageFormat: "Type '{0}' handles messages but is not decorated with [InboxConsumer]. Without idempotency, duplicate messages will cause side-effect duplication.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Consumers should implement the Inbox pattern via [InboxConsumer] to handle at-least-once delivery safely.");

    /// <summary>
    /// The diagnostic identifier for infinite retry configurations.
    /// </summary>
    public const string InfiniteRetriesDiagnosticId = "OUTBOX004";

    /// <summary>
    /// The diagnostic rule descriptor for infinite retry configurations.
    /// </summary>
    public static readonly DiagnosticDescriptor InfiniteRetriesRule = new(
        id: InfiniteRetriesDiagnosticId,
        title: "Potentially infinite retry configuration",
        messageFormat: "MaxAttempts = {0} detected. Values <= 0 or > 100 indicate a likely misconfiguration that can cause message queue buildup.",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Configure MaxAttempts to a reasonable finite value (1-50) to prevent infinite retry loops.");

    /// <summary>
    /// The diagnostic identifier for missing serializer configurations.
    /// </summary>
    public const string SerializationConfigDiagnosticId = "OUTBOX005";

    /// <summary>
    /// The diagnostic rule descriptor for missing serializer configurations.
    /// </summary>
    public static readonly DiagnosticDescriptor SerializationConfigRule = new(
        id: SerializationConfigDiagnosticId,
        title: "Missing serializer in Outbox options",
        messageFormat: "No configured JsonSerializerContext was found.",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Configure a source-generated or AOT-safe serializer on OutboxOptions.");

    /// <summary>
    /// The diagnostic identifier for unregistered AOT JSON types.
    /// </summary>
    public const string MissingJsonSerializableDiagnosticId = "OUTBOX013";

    /// <summary>
    /// The diagnostic rule descriptor for unregistered AOT JSON types.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingJsonSerializableRule = new(
        id: MissingJsonSerializableDiagnosticId,
        title: "Message type not registered for AOT JSON serialization",
        messageFormat: "The message type '{0}' is not registered using [JsonSerializable] in the JsonSerializerContext.",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "All messages must be registered with [JsonSerializable] in your JsonSerializerContext for NativeAOT support.",
        customTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

    /// <summary>
    /// The diagnostic identifier for missing integration event outbox attributes.
    /// </summary>
    public const string MissingOutboxMessageAttributeDiagnosticId = "OUTBOX006";

    /// <summary>
    /// The diagnostic rule descriptor for missing integration event outbox attributes.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingOutboxMessageAttributeRule = new(
        id: MissingOutboxMessageAttributeDiagnosticId,
        title: "IIntegrationEvent implementer missing [OutboxMessage] attribute",
        messageFormat: "Type '{0}' implements IIntegrationEvent but is missing [OutboxMessage(\"alias\")]. " +
                       "The NativeAOT message type resolver will throw KeyNotFoundException at runtime.",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "All types that implement IIntegrationEvent and are stored via the Outbox must be " +
                     "decorated with [OutboxMessage(\"alias\")] to guarantee that the source-generated " +
                     "type resolver (NativeAOT-safe) can serialize and deserialize them.");

    /// <summary>
    /// The diagnostic identifier for null transaction invocations.
    /// </summary>
    public const string NullTransactionDiagnosticId = "OUTBOX007";

    /// <summary>
    /// The diagnostic rule descriptor for null transaction invocations.
    /// </summary>
    public static readonly DiagnosticDescriptor NullTransactionRule = new(
        id: NullTransactionDiagnosticId,
        title: "StoreAsync called without a transaction",
        messageFormat: "StoreAsync is called with a null transaction. " +
                       "The Transactional Outbox pattern requires the outbox write to be part of the same DB transaction as the business operation. " +
                       "Pass the active transaction to guarantee atomicity.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Outbox writes must be transactional. If StoreAsync receives null, messages may be " +
                     "persisted without the accompanying business transaction, violating the exactly-once guarantee.");

    /// <summary>
    /// The diagnostic identifier for abandoned message builder instances.
    /// </summary>
    public const string AbandonedBuilderDiagnosticId = "OUTBOX008";

    /// <summary>
    /// The diagnostic rule descriptor for abandoned message builder instances.
    /// </summary>
    public static readonly DiagnosticDescriptor AbandonedBuilderRule = new(
        id: AbandonedBuilderDiagnosticId,
        title: "Outbox message builder abandoned",
        messageFormat: "The outbox message builder was abandoned without calling StoreAsync. The message will not be saved.",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Always call StoreAsync(transaction) at the end of the IOutboxMessageBuilder chain.");

    /// <summary>
    /// The diagnostic identifier for zero maximum retries configurations.
    /// </summary>
    public const string ZeroMaxRetriesDiagnosticId = "OUTBOX009";

    /// <summary>
    /// The diagnostic rule descriptor for zero maximum retries configurations.
    /// </summary>
    public static readonly DiagnosticDescriptor ZeroMaxRetriesRule = new(
        id: ZeroMaxRetriesDiagnosticId,
        title: "MaxRetryCount set to 0",
        messageFormat: "MaxRetryCount is set to 0. All failing messages will be immediately dead-lettered without any retries.",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Setting MaxRetryCount to 0 disables the transient fault tolerance mechanism of the outbox pattern.");

    /// <summary>
    /// The diagnostic identifier for default dispatch result returns.
    /// </summary>
    public const string DefaultDispatchResultDiagnosticId = "OUTBOX012";

    /// <summary>
    /// The diagnostic rule descriptor for default dispatch result returns.
    /// </summary>
    public static readonly DiagnosticDescriptor DefaultDispatchResultRule = new(
        id: DefaultDispatchResultDiagnosticId,
        title: "IBrokerPublisher returns default(DispatchResult)",
        messageFormat: "'{0}.PublishRawAsync' returns 'default' which is an invalid DispatchResult state (Success=false, ShouldRetry=false, Error=null). " +
                       "Use DispatchResult.Ok(), DispatchResult.FailAndRetry(ex), or DispatchResult.FailFatal(ex) instead.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Returning default(DispatchResult) from IBrokerPublisher.PublishRawAsync causes the dispatcher " +
                     "to dead-letter the message with no error context. Always return a valid DispatchResult factory result.");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            MissingIdRule,
            MissingAliasRule,
            NonIdempotentConsumerRule,
            InfiniteRetriesRule,
            SerializationConfigRule,
            MissingJsonSerializableRule,
            MissingOutboxMessageAttributeRule,
            NullTransactionRule,
            AbandonedBuilderRule,
            ZeroMaxRetriesRule,
            DefaultDispatchResultRule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // OUTBOX001 + OUTBOX002: Scan IOutbox<T>.StoreAsync / .Publish<T> call sites
        context.RegisterSyntaxNodeAction(AnalyzeGenericInvocation, SyntaxKind.InvocationExpression);

        // OUTBOX007: Scan StoreAsync calls for null transaction argument
        context.RegisterSyntaxNodeAction(AnalyzeStoreAsyncNullTransaction, SyntaxKind.InvocationExpression);

        // OUTBOX003: Scan class declarations to detect message handler types missing [InboxConsumer]
        context.RegisterSymbolAction(AnalyzeConsumerType, SymbolKind.NamedType);

        // OUTBOX004: Scan object creation expressions for retry policy constructors with bad MaxAttempts
        context.RegisterSyntaxNodeAction(AnalyzeRetryPolicyCreation, SyntaxKind.ObjectCreationExpression);

        // OUTBOX005: Scan AddOutbox() call sites to detect missing serializer configuration
        context.RegisterSyntaxNodeAction(AnalyzeOutboxOptionsConfiguration, SyntaxKind.InvocationExpression);

        // OUTBOX006 (shipped): Scan type declarations for IIntegrationEvent implementers missing [OutboxMessage]
        context.RegisterSymbolAction(AnalyzeIntegrationEventType, SymbolKind.NamedType);

        // OUTBOX013: Validate all [OutboxMessage] are registered in JsonSerializerContext
        context.RegisterSymbolAction(AnalyzeJsonSerializerContext, SymbolKind.NamedType);
        
        // OUTBOX008, OUTBOX009: Use Operations for easier analysis
        context.RegisterOperationAction(AnalyzeAbandonedBuilder, OperationKind.ExpressionStatement);
        context.RegisterOperationAction(AnalyzeAssignment, OperationKind.SimpleAssignment);

        // OUTBOX010: Detect return default in IBrokerPublisher.PublishRawAsync implementations
        context.RegisterSymbolAction(AnalyzeBrokerPublisherDefaultReturn, SymbolKind.NamedType);
    }


    // -------------------------------------------------------------------------
    // OUTBOX001 + OUTBOX002
    // -------------------------------------------------------------------------
    private static void AnalyzeGenericInvocation(SyntaxNodeAnalysisContext ctx)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;

        if (ctx.SemanticModel.GetSymbolInfo(invocation, ctx.CancellationToken).Symbol is not IMethodSymbol method)
            return;

        // Only care about methods on types that implement IOutbox.
        // Use namespace prefix check rather than exact FQN to handle both:
        //   - The real IOutbox (EricksonLopez.Outbox.IOutbox)
        //   - Any generic wrapper (EricksonLopez.Outbox.IOutbox<T>)
        // Prefix check is more precise than Contains() but still correct.
        var containingType = method.ContainingType;
        bool implementsIOutbox =
            (containingType.Name == "IOutbox" &&
             containingType.ContainingNamespace?.ToDisplayString().StartsWith("EricksonLopez.Outbox", StringComparison.Ordinal) == true) ||
            containingType.AllInterfaces.Any(i =>
                i.Name == "IOutbox" &&
                i.ContainingNamespace?.ToDisplayString().StartsWith("EricksonLopez.Outbox", StringComparison.Ordinal) == true);

        if (!implementsIOutbox)
            return;

        if (method.Name is not ("StoreAsync" or "Publish"))
            return;

        foreach (var typeArg in method.TypeArguments)
        {
            if (typeArg is INamedTypeSymbol namedType)
            {
                // OUTBOX002 — Missing [OutboxMessage] attribute
                var hasAttr = namedType.GetAttributes().Any(a =>
                    a.AttributeClass?.Name is "OutboxMessageAttribute" or "OutboxMessage" &&
                    (a.AttributeClass.ContainingNamespace is null ||
                     a.AttributeClass.ContainingNamespace.ToDisplayString().StartsWith("EricksonLopez.Outbox", StringComparison.Ordinal) ||
                     a.AttributeClass.ContainingNamespace.IsGlobalNamespace));
                if (!hasAttr)
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(MissingAliasRule, invocation.GetLocation(), namedType.Name));
                }

                // OUTBOX001 — Missing 'Guid Id' property
                var hasIdProp = namedType.GetMembers()
                    .OfType<IPropertySymbol>()
                    .Any(p => p.Name == "Id" &&
                              p.Type.Name == "Guid");

                if (!hasIdProp)
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(MissingIdRule, invocation.GetLocation(), namedType.Name));
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // OUTBOX003 â€” Consumer not decorated with [InboxConsumer]
    // Heuristic: any class implementing an interface named IConsumer<T>, IMessageHandler<T>,
    // or containing "Consumer" or "Handler" in the name, without [InboxConsumer] attribute.
    // -------------------------------------------------------------------------
    private static void AnalyzeConsumerType(SymbolAnalysisContext ctx)
    {
        var typeSymbol = (INamedTypeSymbol)ctx.Symbol;

        if (typeSymbol.TypeKind != TypeKind.Class || typeSymbol.IsAbstract)
            return;

        bool looksLikeConsumer = typeSymbol.AllInterfaces.Any(i =>
            (i.Name == "IConsumer" || i.Name == "IHandleMessages" || i.Name == "IMessageHandler"));

        if (!looksLikeConsumer)
            return;

        // Check it doesn't already have [InboxConsumer] or [IdempotentConsumer]
        bool hasInboxConsumerAttr = typeSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name is "InboxConsumerAttribute" or "InboxConsumer" or "IdempotentConsumerAttribute" or "IdempotentConsumer");

        if (!hasInboxConsumerAttr)
        {
            var location = typeSymbol.Locations[0];
            ctx.ReportDiagnostic(Diagnostic.Create(
                NonIdempotentConsumerRule,
                location,
                typeSymbol.Name));
        }
    }

    // -------------------------------------------------------------------------
    // OUTBOX004 — MaxAttempts <= 0 or > 100 in retry policy constructor
    // -------------------------------------------------------------------------
    private static void AnalyzeRetryPolicyCreation(SyntaxNodeAnalysisContext ctx)
    {
        var creation = (ObjectCreationExpressionSyntax)ctx.Node;

        if (ctx.SemanticModel.GetSymbolInfo(creation, ctx.CancellationToken).Symbol is not IMethodSymbol constructor)
            return;

        if (constructor.ContainingType.Name.IndexOf("RetryPolicy", StringComparison.OrdinalIgnoreCase) < 0)
            return;

        if (creation.ArgumentList is null)
            return;

        int index = 0;
        foreach (var arg in creation.ArgumentList.Arguments)
        {
            string? paramName = arg.NameColon is not null
                ? arg.NameColon.Name.Identifier.Text
                : (index < constructor.Parameters.Length ? constructor.Parameters[index].Name : null);
            index++;

            if (!string.Equals(paramName, "MaxAttempts", StringComparison.OrdinalIgnoreCase))
                continue;

            var constVal = ctx.SemanticModel.GetConstantValue(arg.Expression, ctx.CancellationToken);
            if (constVal.HasValue && constVal.Value is int maxAttempts)
            {
                if (maxAttempts <= 0 || maxAttempts > 100)
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        InfiniteRetriesRule,
                        arg.GetLocation(),
                        maxAttempts));
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // OUTBOX005 — AddOutbox called without serializer configuration
    // -------------------------------------------------------------------------
    private static void AnalyzeOutboxOptionsConfiguration(SyntaxNodeAnalysisContext ctx)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;

        if (ctx.SemanticModel.GetSymbolInfo(invocation, ctx.CancellationToken).Symbol is not IMethodSymbol method)
            return;

        if (method.Name != "AddOutbox")
            return;

        var args = invocation.ArgumentList.Arguments;
        if (args.Count == 0)
            return;

        var lambdaBody = (args[0].Expression as LambdaExpressionSyntax)?.Body;
        if (lambdaBody is null)
            return;

        bool hasSerializerCall = lambdaBody.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(inner =>
            {
                string? calledName = inner.Expression switch
                {
                    MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
                    IdentifierNameSyntax id => id.Identifier.Text,
                    _ => null
                };
                return calledName is "UseSerializer" or "UseGeneratedTypes" or "UseGeneratedTypesAndSerialization";
            });

        if (!hasSerializerCall)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                SerializationConfigRule,
                invocation.GetLocation()));
        }
    }

    // -------------------------------------------------------------------------
    // OUTBOX006 (shipped) — IIntegrationEvent without [OutboxMessage] attribute
    // -------------------------------------------------------------------------
    private static void AnalyzeIntegrationEventType(SymbolAnalysisContext ctx)
    {
        var typeSymbol = (INamedTypeSymbol)ctx.Symbol;

        // Only concrete classes and structs — skip interfaces and abstract types
        if (typeSymbol.IsAbstract || typeSymbol.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            return;

        // Check if type implements IIntegrationEvent
        bool implementsIntegrationEvent = typeSymbol.AllInterfaces.Any(iface =>
            iface.Name == "IIntegrationEvent" &&
            iface.ContainingNamespace?.ToDisplayString().IndexOf("EricksonLopez", StringComparison.Ordinal) >= 0);

        if (!implementsIntegrationEvent)
            return;

        // Check if type has [OutboxMessage] attribute
        bool hasAttribute = typeSymbol.GetAttributes().Any(attr =>
            attr.AttributeClass?.Name is "OutboxMessageAttribute" or "OutboxMessage");

        if (!hasAttribute)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                MissingOutboxMessageAttributeRule,
                typeSymbol.Locations[0],
                typeSymbol.Name));
        }
    }

    // -------------------------------------------------------------------------
    // OUTBOX006 — StoreAsync with literal null transaction argument
    // -------------------------------------------------------------------------
    private static void AnalyzeStoreAsyncNullTransaction(SyntaxNodeAnalysisContext ctx)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;

        if (ctx.SemanticModel.GetSymbolInfo(invocation, ctx.CancellationToken).Symbol is not IMethodSymbol method)
            return;

        if (method.Name != "StoreAsync")
            return;

        var containingType = method.ContainingType;
        bool implementsIOutbox =
            (containingType.Name == "IOutbox" &&
             containingType.ContainingNamespace?.ToDisplayString().StartsWith("EricksonLopez.Outbox", StringComparison.Ordinal) == true) ||
            containingType.AllInterfaces.Any(i =>
                i.Name == "IOutbox" &&
                i.ContainingNamespace?.ToDisplayString().StartsWith("EricksonLopez.Outbox", StringComparison.Ordinal) == true);

        if (!implementsIOutbox)
            return;

        int index = 0;
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            if (IsNullLiteral(arg.Expression))
            {
                bool isTransactionParam = false;
                if (arg.NameColon is not null)
                {
                    var paramName = arg.NameColon.Name.Identifier.Text;
                    isTransactionParam = paramName.Equals("transaction", StringComparison.OrdinalIgnoreCase) ||
                                         paramName.Equals("transactionContext", StringComparison.OrdinalIgnoreCase) ||
                                         paramName.Equals("tx", StringComparison.OrdinalIgnoreCase) ||
                                         paramName.Equals("context", StringComparison.OrdinalIgnoreCase);
                }
                else if (index == 1)
                {
                    isTransactionParam = true;
                }
                else if (index < method.Parameters.Length)
                {
                    var param = method.Parameters[index];
                    isTransactionParam = param.Name is "transaction" or "transactionContext" or "context" or "tx" ||
                                         param.Type.Name is "IOutboxTransactionContext" or "DbTransaction" or "IDbTransaction";
                }

                if (isTransactionParam)
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        NullTransactionRule,
                        arg.GetLocation()));
                    return;
                }
            }
            index++;
        }
    }

    private static bool IsNullLiteral(ExpressionSyntax expr)
    {
        while (expr is CastExpressionSyntax or PostfixUnaryExpressionSyntax or ParenthesizedExpressionSyntax)
        {
            if (expr is CastExpressionSyntax cast) expr = cast.Expression;
            else if (expr is PostfixUnaryExpressionSyntax postfix) expr = postfix.Operand;
            else if (expr is ParenthesizedExpressionSyntax paren) expr = paren.Expression;
        }

        return expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NullLiteralExpression);
    }

    // -------------------------------------------------------------------------
    // OUTBOX008 — Abandoned builder
    // -------------------------------------------------------------------------
    private static void AnalyzeAbandonedBuilder(OperationAnalysisContext ctx)
    {
        var expressionStatement = (IExpressionStatementOperation)ctx.Operation;

        var type = expressionStatement.Operation.Type;
        if (type?.ContainingNamespace?.ToDisplayString().StartsWith("EricksonLopez.Outbox", StringComparison.Ordinal) != true)
            return;

        // Check if the discarded expression has a builder return type
        bool isBuilder =
            type.Name is "OutboxMessageBuilder" or "IOutboxMessageBuilder" or "OutboxOptionsBuilder" or "OutboxPipelineBuilder" or "IOutboxBuilder";

        // Also check implemented interfaces in case it's a custom wrapper
        if (!isBuilder && type is INamedTypeSymbol namedType)
        {
            isBuilder = namedType.AllInterfaces.Any(i =>
                i.Name == "IOutboxMessageBuilder" &&
                i.ContainingNamespace?.ToDisplayString().StartsWith(
                    "EricksonLopez.Outbox", StringComparison.Ordinal) == true);
        }

        if (isBuilder)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                AbandonedBuilderRule,
                expressionStatement.Syntax.GetLocation(),
                type.Name));
        }
    }

    // -------------------------------------------------------------------------
    // OUTBOX009 — MaxRetryCount = 0
    // -------------------------------------------------------------------------
    private static void AnalyzeAssignment(OperationAnalysisContext ctx)
    {
        var assignment = (ISimpleAssignmentOperation)ctx.Operation;

        if (assignment.Target is IPropertyReferenceOperation propRef &&
            propRef.Property.Name == "MaxRetryCount" &&
            propRef.Property.ContainingType?.Name == "OutboxOptions")
        {
            if (assignment.Value is ILiteralOperation literal &&
                literal.ConstantValue.HasValue &&
                literal.ConstantValue.Value is int val && val == 0)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(ZeroMaxRetriesRule, assignment.Syntax.GetLocation()));
            }
        }
    }

    // -------------------------------------------------------------------------
    // OUTBOX013 — Missing JsonSerializable attribute on JsonSerializerContext
    // -------------------------------------------------------------------------
    private static void AnalyzeJsonSerializerContext(SymbolAnalysisContext ctx)
    {
        var contextType = (INamedTypeSymbol)ctx.Symbol;
        if (!InheritsFromJsonSerializerContext(contextType))
            return;

        var jsonSerializableTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var attr in contextType.GetAttributes())
        {
            if (attr.AttributeClass?.Name == "JsonSerializableAttribute" &&
                attr.ConstructorArguments.Length > 0 &&
                attr.ConstructorArguments[0].Value is ITypeSymbol serializableType)
            {
                jsonSerializableTypes.Add(serializableType);
            }
        }

        var stack = new Stack<INamespaceSymbol>();
        stack.Push(ctx.Compilation.GlobalNamespace);

        while (stack.Count > 0)
        {
            var currentNs = stack.Pop();
            foreach (var member in currentNs.GetMembers())
            {
                if (member is INamespaceSymbol nestedNs)
                {
                    stack.Push(nestedNs);
                }
                else if (member is INamedTypeSymbol typeSymbol)
                {
                    bool hasOutboxMessageAttr = typeSymbol.GetAttributes().Any(attr =>
                        attr.AttributeClass?.Name is "OutboxMessageAttribute" or "OutboxMessage");

                    if (hasOutboxMessageAttr && !jsonSerializableTypes.Contains(typeSymbol))
                    {
                        ctx.ReportDiagnostic(Diagnostic.Create(
                            MissingJsonSerializableRule,
                            typeSymbol.Locations[0],
                            typeSymbol.Name));
                    }
                }
            }
        }
    }

    private static bool InheritsFromJsonSerializerContext(INamedTypeSymbol typeSymbol)
    {
        for (var cur = typeSymbol.BaseType; cur is not null; cur = cur.BaseType)
        {
            if (cur.Name == "JsonSerializerContext" && cur.ContainingNamespace?.ToDisplayString() == "System.Text.Json.Serialization")
                return true;
        }
        return false;
    }

    // -------------------------------------------------------------------------
    // OUTBOX012 — IBrokerPublisher returning default(DispatchResult)
    // G7.1-FIX: Detects `return default;` or `return default(DispatchResult);` inside
    // any PublishRawAsync override, which produces invalid DispatchResult state.
    // -------------------------------------------------------------------------
    private static void AnalyzeBrokerPublisherDefaultReturn(SymbolAnalysisContext ctx)
    {
        var typeSymbol = (INamedTypeSymbol)ctx.Symbol;

        // Check if this type implements IBrokerPublisher
        bool implementsPublisher = typeSymbol.AllInterfaces.Any(static i =>
            i.Name == "IBrokerPublisher" &&
            i.ContainingNamespace?.ToDisplayString() == "EricksonLopez.Outbox");

        if (!implementsPublisher)
            return;

        // Find non-abstract PublishRawAsync methods
        foreach (var member in typeSymbol.GetMembers("PublishRawAsync"))
        {
            if (member is IMethodSymbol { IsAbstract: false } publishMethod)
            {
                foreach (var syntaxRef in publishMethod.DeclaringSyntaxReferences)
                {
                    var syntax = syntaxRef.GetSyntax(ctx.CancellationToken);
                    if (syntax is MethodDeclarationSyntax methodDecl)
                    {
                        foreach (var ret in methodDecl.DescendantNodes().OfType<ReturnStatementSyntax>())
                        {
                            var expr = ret.Expression;

                            bool isDefaultLiteral = expr is LiteralExpressionSyntax lit &&
                                lit.IsKind(SyntaxKind.DefaultLiteralExpression);
                            bool isDefaultExpression = expr is DefaultExpressionSyntax;

                            if (isDefaultLiteral || isDefaultExpression)
                            {
                                ctx.ReportDiagnostic(Diagnostic.Create(
                                    DefaultDispatchResultRule,
                                    ret.GetLocation(),
                                    typeSymbol.Name));
                            }
                        }
                    }
                }
            }
        }
    }
}





