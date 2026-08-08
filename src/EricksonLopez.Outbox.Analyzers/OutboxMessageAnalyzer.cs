using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EricksonLopez.Outbox.Analyzers;

/// <summary>
/// Roslyn Analyzer that enforces Outbox best practices at design time.
/// 
/// Rules:
///   OUTBOX001 â€” Message type is missing 'Guid Id' property.
///   OUTBOX002 â€” Type passed to IOutbox is missing [OutboxMessage] attribute / alias.
///   OUTBOX003 â€” Consumer is not implementing idempotency (missing [InboxConsumer]).
///   OUTBOX004 â€” RetryPolicy configured with infinite or zero MaxAttempts.
///   OUTBOX005 â€” OutboxOptions configured without a serializer.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OutboxMessageAnalyzer : DiagnosticAnalyzer
{
    // OUTBOX001 â€” Missing 'Guid Id' property on outbox message type
    public const string MissingIdDiagnosticId = "OUTBOX001";
    public static readonly DiagnosticDescriptor MissingIdRule = new(
        id: MissingIdDiagnosticId,
        title: "Message type missing 'Guid Id' property",
        messageFormat: "Type '{0}' is missing a public 'Guid Id' property required for outbox identification.",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Outbox messages must have an explicit 'Guid Id' property to guarantee unique delivery tracking.");

    // OUTBOX002 â€” Missing [OutboxMessage] attribute / alias
    public const string MissingAliasDiagnosticId = "OUTBOX002";
    public static readonly DiagnosticDescriptor MissingAliasRule = new(
        id: MissingAliasDiagnosticId,
        title: "Missing [OutboxMessage] attribute",
        messageFormat: "Type '{0}' is missing the [OutboxMessage(\"alias\")] attribute. NativeAOT serialization will fail at runtime.",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "All types stored via IOutbox<T>.StoreAsync must be decorated with [OutboxMessage(\"alias\")] to guarantee NativeAOT-safe, reflection-free serialization.");

    // OUTBOX003 â€” Consumer not idempotent
    public const string NonIdempotentConsumerDiagnosticId = "OUTBOX003";
    public static readonly DiagnosticDescriptor NonIdempotentConsumerRule = new(
        id: NonIdempotentConsumerDiagnosticId,
        title: "Consumer is not idempotent",
        messageFormat: "Type '{0}' handles messages but is not decorated with [InboxConsumer]. Without idempotency, duplicate messages will cause side-effect duplication.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Consumers should implement the Inbox pattern via [InboxConsumer] to handle at-least-once delivery safely.");

    // OUTBOX004 â€” Infinite retries
    public const string InfiniteRetriesDiagnosticId = "OUTBOX004";
    public static readonly DiagnosticDescriptor InfiniteRetriesRule = new(
        id: InfiniteRetriesDiagnosticId,
        title: "Potentially infinite retry configuration",
        messageFormat: "MaxAttempts = {0} detected. Values <= 0 or > 100 indicate a likely misconfiguration that can cause message queue buildup.",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Configure MaxAttempts to a reasonable finite value (1â€“50) to prevent infinite retry loops.");

    // OUTBOX005 â€” Missing Serializer Config
    public const string SerializationConfigDiagnosticId = "OUTBOX005";
    public static readonly DiagnosticDescriptor SerializationConfigRule = new(
        id: SerializationConfigDiagnosticId,
        title: "Missing serializer in Outbox options",
        messageFormat: "No configured JsonSerializerContext was found.",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Configure a source-generated or AOT-safe serializer on OutboxOptions.");

    // OUTBOX006 — Missing [JsonSerializable] in JsonSerializerContext
    public const string MissingJsonSerializableDiagnosticId = "OUTBOX006";
    public static readonly DiagnosticDescriptor MissingJsonSerializableRule = new(
        id: MissingJsonSerializableDiagnosticId,
        title: "Message type not registered for AOT JSON serialization",
        messageFormat: "The message type '{0}' is not registered using [JsonSerializable] in the JsonSerializerContext.",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "All messages must be registered with [JsonSerializable] in your JsonSerializerContext for NativeAOT support.",
        customTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

    // OUTBOX011 — IIntegrationEvent without [OutboxMessage] attribute
    public const string MissingOutboxMessageAttributeDiagnosticId = "OUTBOX011";
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

    // OUTBOX007 — StoreAsync called with null transaction (atomicity violation)
    // FIX-20: Detect when StoreAsync is called with a null literal as the transaction argument.
    // The Transactional Outbox pattern REQUIRES a transaction — calling StoreAsync without one
    // breaks atomicity: the message may be persisted even if the surrounding business operation
    // rolls back (or vice versa). This is a correctness issue, not just a style issue.
    public const string NullTransactionDiagnosticId = "OUTBOX007";
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
                     
    // OUTBOX008 — Abandoned builder
    public const string AbandonedBuilderDiagnosticId = "OUTBOX008";
    public static readonly DiagnosticDescriptor AbandonedBuilderRule = new(
        id: AbandonedBuilderDiagnosticId,
        title: "Outbox message builder abandoned",
        messageFormat: "The outbox message builder was abandoned without calling StoreAsync. The message will not be saved.",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Always call StoreAsync(transaction) at the end of the IOutboxMessageBuilder chain.");

    // OUTBOX009 — MaxRetryCount = 0
    public const string ZeroMaxRetriesDiagnosticId = "OUTBOX009";
    public static readonly DiagnosticDescriptor ZeroMaxRetriesRule = new(
        id: ZeroMaxRetriesDiagnosticId,
        title: "MaxRetryCount set to 0",
        messageFormat: "MaxRetryCount is set to 0. All failing messages will be immediately dead-lettered without any retries.",
        category: "Configuration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Setting MaxRetryCount to 0 disables the transient fault tolerance mechanism of the outbox pattern.");

    // OUTBOX012 — IBrokerPublisher returning default(DispatchResult)
    // G7.1-FIX: Detect when a PublishRawAsync implementation returns `default` (struct zero-value).
    // default(DispatchResult) = { Success=false, ShouldRetry=false, Error=null } — an invalid state
    // that causes the dispatcher to dead-letter the message with no error information.
    // NOTE: OUTBOX010 is owned by TransactionRequiredAnalyzer. We use OUTBOX012 here.
    public const string DefaultDispatchResultDiagnosticId = "OUTBOX012";
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

        // OUTBOX011: Scan type declarations for IIntegrationEvent implementers missing [OutboxMessage]
        context.RegisterSymbolAction(AnalyzeIntegrationEventType, SymbolKind.NamedType);
        
        // OUTBOX006: Validate all [OutboxMessage] are registered in JsonSerializerContext
        context.RegisterCompilationAction(AnalyzeJsonSerializerContext);
        
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

        if (ctx.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
            return;

        // Only care about methods on types that implement IOutbox.
        // Use namespace prefix check rather than exact FQN to handle both:
        //   - The real IOutbox (EricksonLopez.Outbox.IOutbox)
        //   - Any generic wrapper (EricksonLopez.Outbox.IOutbox<T>)
        // Prefix check is more precise than Contains() but still correct.
        var containingType = method.ContainingType;
        bool implementsIOutbox =
            (containingType.Name == "IOutbox" &&
             containingType.ContainingNamespace?.ToDisplayString().StartsWith("EricksonLopez.Outbox", System.StringComparison.Ordinal) == true) ||
            containingType.AllInterfaces.Any(i =>
                i.Name == "IOutbox" &&
                i.ContainingNamespace?.ToDisplayString().StartsWith("EricksonLopez.Outbox", System.StringComparison.Ordinal) == true);

        if (!implementsIOutbox)
            return;

        if (method.Name is not ("StoreAsync" or "Publish"))
            return;

        foreach (var typeArg in method.TypeArguments)
        {
            if (typeArg is not INamedTypeSymbol namedType)
                continue;

            // OUTBOX002 — Missing [OutboxMessage] attribute
            // Use attribute class simple name check — ToDisplayString() may not resolve to full FQN
            // in partial compilations (e.g., in analyzer unit tests with stub types).
            // We accept both "OutboxMessageAttribute" and "OutboxMessage" forms.
            var hasAttr = namedType.GetAttributes().Any(a =>
                a.AttributeClass?.Name is "OutboxMessageAttribute" or "OutboxMessage" &&
                (a.AttributeClass.ContainingNamespace is null ||
                 a.AttributeClass.ContainingNamespace.ToDisplayString().StartsWith("EricksonLopez.Outbox", System.StringComparison.Ordinal) ||
                 a.AttributeClass.ContainingNamespace.IsGlobalNamespace));
            if (!hasAttr)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(MissingAliasRule, invocation.GetLocation(), namedType.Name));
            }

            // OUTBOX001 — Missing 'Guid Id' property
            // FIX: Use exact FQN comparison (ToDisplayString() == "System.Guid") instead of the
            // fragile SpecialType.None + Contains("Guid") combo. SpecialType.None is always true for
            // Guid (it's not a primitive), making it redundant. Using the full qualified name is
            // explicit and immune to accidental matching on user-defined types named "MyGuid" etc.
            var hasIdProp = namedType.GetMembers()
                .OfType<IPropertySymbol>()
                .Any(p => p.Name == "Id" &&
                          p.Type.ToDisplayString() == "System.Guid");

            if (!hasIdProp)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(MissingIdRule, invocation.GetLocation(), namedType.Name));
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
        if (ctx.Symbol is not INamedTypeSymbol typeSymbol)
            return;

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
            var location = typeSymbol.Locations.FirstOrDefault();
            if (location is not null)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    NonIdempotentConsumerRule,
                    location,
                    typeSymbol.Name));
            }
        }
    }

    // -------------------------------------------------------------------------
    // OUTBOX004 â€” MaxAttempts <= 0 or > 100 in retry policy constructor
    // -------------------------------------------------------------------------
    private static void AnalyzeRetryPolicyCreation(SyntaxNodeAnalysisContext ctx)
    {
        var creation = (ObjectCreationExpressionSyntax)ctx.Node;

        if (ctx.SemanticModel.GetSymbolInfo(creation).Symbol is not IMethodSymbol constructor)
            return;

        var typeName = constructor.ContainingType.Name;
        if (typeName.IndexOf("RetryPolicy", System.StringComparison.OrdinalIgnoreCase) < 0)
            return;

        // Look for a MaxAttempts parameter in the constructor arguments
        var argList = creation.ArgumentList?.Arguments;
        if (argList is null) return;

        foreach (var arg in argList)
        {
            string? paramName = null;
            if (arg.NameColon is not null)
            {
                paramName = arg.NameColon.Name.Identifier.Text;
            }
            else if (argList.HasValue)
            {
                int index = argList.Value.IndexOf(arg);
                if (index >= 0 && index < constructor.Parameters.Length)
                {
                    paramName = constructor.Parameters[index].Name;
                }
            }

            if (paramName is null ||
                !paramName.Equals("MaxAttempts", System.StringComparison.OrdinalIgnoreCase))
                continue;

            // Try to get the constant value
            var constVal = ctx.SemanticModel.GetConstantValue(arg.Expression);
            if (!constVal.HasValue || constVal.Value is not int maxAttempts)
                continue;

            if (maxAttempts <= 0 || maxAttempts > 100)
            {
                ctx.ReportDiagnostic(Diagnostic.Create(
                    InfiniteRetriesRule,
                    arg.GetLocation(),
                    maxAttempts));
            }
        }
    }

    // -------------------------------------------------------------------------
    // OUTBOX005 — AddOutbox() call without UseSerializer() / UseGeneratedTypes()
    // -------------------------------------------------------------------------
    private static void AnalyzeOutboxOptionsConfiguration(SyntaxNodeAnalysisContext ctx)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;

        if (ctx.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
            return;

        if (!method.Name.Equals("AddOutbox", System.StringComparison.Ordinal))
            return;

        // Check that the argument is a lambda that calls UseSerializer or UseGeneratedTypes.
        var args = invocation.ArgumentList.Arguments;
        if (args.Count == 0) return;

        var lambdaArg = args[0].Expression;
        if (lambdaArg is not (SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax))
            return;

        // FIX-11: Use semantic invocation analysis instead of fragile string matching.
        //
        // Previous bug: the analyzer searched for the literal string "Serializer" inside
        // the lambda body TEXT. This produced:
        //   - False negatives: a comment like "// TODO: add serializer" would suppress the warning.
        //   - False positives: any variable named "mySerializer" would suppress it even without calling
        //     UseSerializer().
        //
        // Fix: Walk the syntax tree of the lambda body to find actual invocation expressions
        // whose method name is UseSerializer or UseGeneratedTypes. This is purely syntactic
        // (no full semantic analysis needed here) because the exact method signature is less
        // important than whether the user intends to configure a serializer.
        //
        // We deliberately use a syntactic (name-based) check rather than a full symbol resolution
        // because this analyzer runs on incomplete/building compilations and the OutboxOptions
        // type may not be fully resolved in all analyzer execution contexts.
        var lambdaBody = (lambdaArg as SimpleLambdaExpressionSyntax)?.Body
                       ?? (lambdaArg as ParenthesizedLambdaExpressionSyntax)?.Body as SyntaxNode;

        if (lambdaBody is null) return;

        // Collect all invocation expressions within the lambda body.
        var hasSerializerCall = false;
        foreach (var node in lambdaBody.DescendantNodes())
        {
            if (node is not InvocationExpressionSyntax innerInvocation)
                continue;

            // Extract the method name from the invocation expression.
            string? calledMethodName = null;
            if (innerInvocation.Expression is MemberAccessExpressionSyntax memberAccess)
                calledMethodName = memberAccess.Name.Identifier.Text;
            else if (innerInvocation.Expression is IdentifierNameSyntax identifier)
                calledMethodName = identifier.Identifier.Text;

            if (calledMethodName is "UseSerializer" or "UseGeneratedTypes" or "UseGeneratedTypesAndSerialization")
            {
                hasSerializerCall = true;
                break;
            }
        }

        if (!hasSerializerCall)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(
                SerializationConfigRule,
                invocation.GetLocation()));
        }
    }

    // -------------------------------------------------------------------------
    // OUTBOX011 — IIntegrationEvent without [OutboxMessage] attribute
    // -------------------------------------------------------------------------
    private static void AnalyzeIntegrationEventType(SymbolAnalysisContext ctx)
    {
        if (ctx.Symbol is not INamedTypeSymbol typeSymbol)
            return;

        // Only concrete classes and structs — skip interfaces and abstract types
        if (typeSymbol.IsAbstract || typeSymbol.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            return;

        // Check if the type implements IIntegrationEvent
        bool implementsIntegrationEvent = typeSymbol.AllInterfaces.Any(iface =>
            iface.Name == "IIntegrationEvent" &&
            iface.ContainingNamespace?.ToDisplayString().IndexOf("EricksonLopez", System.StringComparison.Ordinal) >= 0);

        if (!implementsIntegrationEvent)
            return;

        // Check if [OutboxMessage] attribute is present (simple name check — avoids needing
        // to resolve the full type which would require a compilation reference)
        bool hasOutboxMessageAttribute = typeSymbol.GetAttributes().Any(attr =>
            attr.AttributeClass?.Name is "OutboxMessageAttribute" or "OutboxMessage");

        if (!hasOutboxMessageAttribute)
        {
            var diagnostic = Diagnostic.Create(
                MissingOutboxMessageAttributeRule,
                typeSymbol.Locations.FirstOrDefault() ?? Location.None,
                typeSymbol.Name);
            ctx.ReportDiagnostic(diagnostic);
        }
    }

    // -------------------------------------------------------------------------
    // OUTBOX007 — StoreAsync called with null transaction (atomicity violation)
    // -------------------------------------------------------------------------
    private static void AnalyzeStoreAsyncNullTransaction(SyntaxNodeAnalysisContext ctx)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;

        // Check if this is a call to StoreAsync
        string? methodName = null;
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            methodName = memberAccess.Name.Identifier.Text;
        else if (invocation.Expression is IdentifierNameSyntax identifier)
            methodName = identifier.Identifier.Text;

        if (methodName != "StoreAsync")
            return;

        // Check if the symbol is IOutbox.StoreAsync
        var symbolInfo = ctx.SemanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol method)
            return;

        // Use namespace prefix check to avoid false positives on types with "Outbox" in name
        // (e.g., ShoppingCartOutboxService) while still handling partial compilation contexts
        // where exact FQN may not resolve (e.g., in analyzer unit tests with stub types).
        bool isIOutboxMethod =
            (method.ContainingType.Name == "IOutbox" &&
             method.ContainingType.ContainingNamespace?.ToDisplayString().StartsWith("EricksonLopez.Outbox", System.StringComparison.Ordinal) == true) ||
            method.ContainingType.AllInterfaces.Any(i =>
                i.Name == "IOutbox" &&
                i.ContainingNamespace?.ToDisplayString().StartsWith("EricksonLopez.Outbox", System.StringComparison.Ordinal) == true);

        if (!isIOutboxMethod)
            return;

        // Find the 'transaction' argument (second positional parameter).
        var args = invocation.ArgumentList.Arguments;
        if (args.Count < 2) return;

        // Check if any argument named "transaction" is a null literal, OR
        // if the second positional argument is a null literal.
        foreach (var arg in args)
        {
            // Named argument: transaction: null
            bool isTransactionArg = arg.NameColon?.Name.Identifier.Text == "transaction";
            // Positional: second argument (index 1)
            bool isSecondArg = !isTransactionArg && args.IndexOf(arg) == 1;

            if (!isTransactionArg && !isSecondArg)
                continue;

            if (arg.Expression is not LiteralExpressionSyntax literal)
                continue;

            if (!literal.IsKind(SyntaxKind.NullLiteralExpression))
                continue;

            ctx.ReportDiagnostic(Diagnostic.Create(
                NullTransactionRule,
                arg.GetLocation()));
            break;
        }
    }
    
    // -------------------------------------------------------------------------
    // OUTBOX008 — Abandoned builder
    // -------------------------------------------------------------------------
    private static void AnalyzeAbandonedBuilder(OperationAnalysisContext ctx)
    {
        if (ctx.Operation is not IExpressionStatementOperation expressionStatement)
            return;

        var type = expressionStatement.Operation.Type;
        if (type is null) return;

        // IOutbox.Publish<T>() returns OutboxMessageBuilder<TMessage> (concrete generic type).
        // The builder implements IOutboxMessageBuilder, so we check both type names.
        // Using Name (simple name, not FQN) because generic types surface as
        // "OutboxMessageBuilder" with Arity=1, while the original code incorrectly
        // checked for "IOutboxMessageBuilder" which is the interface — never the actual
        // return type of .Publish<T>().
        bool isBuilder =
            (type.Name is "OutboxMessageBuilder" or "IOutboxMessageBuilder") &&
            type.ContainingNamespace?.ToDisplayString().StartsWith(
                "EricksonLopez.Outbox", System.StringComparison.Ordinal) == true;

        // Also check implemented interfaces in case it's a custom wrapper
        if (!isBuilder && type is INamedTypeSymbol namedType)
        {
            isBuilder = namedType.AllInterfaces.Any(i =>
                i.Name == "IOutboxMessageBuilder" &&
                i.ContainingNamespace?.ToDisplayString().StartsWith(
                    "EricksonLopez.Outbox", System.StringComparison.Ordinal) == true);
        }

        if (isBuilder)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(AbandonedBuilderRule, expressionStatement.Syntax.GetLocation()));
        }
    }

    // -------------------------------------------------------------------------
    // OUTBOX009 — MaxRetryCount = 0
    // -------------------------------------------------------------------------
    private static void AnalyzeAssignment(OperationAnalysisContext ctx)
    {
        if (ctx.Operation is not ISimpleAssignmentOperation assignment)
            return;

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
    // OUTBOX006 — Validate all [OutboxMessage] are registered in JsonSerializerContext
    // -------------------------------------------------------------------------
    private static void AnalyzeJsonSerializerContext(CompilationAnalysisContext ctx)
    {
        var outboxMessages = new System.Collections.Generic.HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var jsonSerializableTypes = new System.Collections.Generic.HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        bool hasJsonSerializerContext = false;

        void VisitNamespace(INamespaceSymbol ns)
        {
            foreach (var member in ns.GetMembers())
            {
                if (member is INamespaceSymbol nestedNs)
                {
                    VisitNamespace(nestedNs);
                }
                else if (member is INamedTypeSymbol typeSymbol)
                {
                    bool hasOutboxMessageAttr = false;
                    foreach (var attr in typeSymbol.GetAttributes())
                    {
                        if (attr.AttributeClass?.Name is "OutboxMessageAttribute" or "OutboxMessage")
                        {
                            hasOutboxMessageAttr = true;
                            break;
                        }
                    }

                    if (hasOutboxMessageAttr)
                    {
                        outboxMessages.Add(typeSymbol);
                    }

                    // Check if type inherits from JsonSerializerContext
                    var baseType = typeSymbol.BaseType;
                    bool isContext = false;
                    while (baseType != null)
                    {
                        if (baseType.Name == "JsonSerializerContext" && baseType.ContainingNamespace?.ToDisplayString() == "System.Text.Json.Serialization")
                        {
                            isContext = true;
                            break;
                        }
                        baseType = baseType.BaseType;
                    }

                    if (isContext)
                    {
                        hasJsonSerializerContext = true;
                        foreach (var attr in typeSymbol.GetAttributes())
                        {
                            if (attr.AttributeClass?.Name == "JsonSerializableAttribute")
                            {
                                if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is ITypeSymbol serializedType)
                                {
                                    jsonSerializableTypes.Add(serializedType);
                                }
                            }
                        }
                    }
                }
            }
        }

        VisitNamespace(ctx.Compilation.GlobalNamespace);

        // If they didn't define any JsonSerializerContext in THIS compilation, we skip validation here.
        // (OUTBOX005 already catches if they didn't call UseSerializer at all).
        if (!hasJsonSerializerContext) return;

        foreach (var msg in outboxMessages)
        {
            if (!jsonSerializableTypes.Contains(msg))
            {
                var diagnostic = Diagnostic.Create(MissingJsonSerializableRule, msg.Locations.FirstOrDefault() ?? Location.None, msg.Name);
                ctx.ReportDiagnostic(diagnostic);
            }
        }
    }

    // -------------------------------------------------------------------------
    // OUTBOX010 — IBrokerPublisher.PublishRawAsync returning default(DispatchResult)
    // G7.1-FIX: Detects `return default;` or `return default(DispatchResult);` inside
    // any PublishRawAsync override, which produces invalid DispatchResult state.
    // -------------------------------------------------------------------------
    private static void AnalyzeBrokerPublisherDefaultReturn(SymbolAnalysisContext ctx)
    {
        if (ctx.Symbol is not INamedTypeSymbol typeSymbol) return;
        if (typeSymbol.TypeKind != TypeKind.Class && typeSymbol.TypeKind != TypeKind.Struct) return;

        // Check if this type implements IBrokerPublisher
        var implementsPublisher = typeSymbol.AllInterfaces.Any(static i =>
            i.Name == "IBrokerPublisher" &&
            i.ContainingNamespace?.ToDisplayString() == "EricksonLopez.Outbox");

        if (!implementsPublisher) return;

        // Find the PublishRawAsync method
        var publishMethod = typeSymbol.GetMembers("PublishRawAsync")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => !m.IsAbstract);

        if (publishMethod is null) return;

        // Get the syntax declarations for this method
        foreach (var syntaxRef in publishMethod.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax(ctx.CancellationToken);
            if (syntax is not MethodDeclarationSyntax methodDecl) continue;

            // Walk all return statements in the method body
            var returnStatements = methodDecl.DescendantNodes()
                .OfType<ReturnStatementSyntax>();

            foreach (var ret in returnStatements)
            {
                var expr = ret.Expression;
                if (expr is null) continue;

                // `return default;` — DefaultLiteralExpressionSyntax
                // `return default(DispatchResult);` — DefaultExpressionSyntax
                bool isDefaultLiteral = expr is LiteralExpressionSyntax lit &&
                    lit.IsKind(SyntaxKind.DefaultLiteralExpression);
                bool isDefaultExpression = expr is DefaultExpressionSyntax;

                if (isDefaultLiteral || isDefaultExpression)
                {
                    ctx.ReportDiagnostic(
                        Diagnostic.Create(
                            DefaultDispatchResultRule,
                            ret.GetLocation(),
                            typeSymbol.Name));
                }
            }
        }
    }
}

