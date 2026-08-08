using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace EricksonLopez.Outbox.SourceGenerators;

/// <summary>
/// Source generator that scans for types annotated with OutboxMessageAttribute 
/// and generates a highly optimized type resolver.
/// </summary>
[Generator]
public sealed class OutboxTypeMappingGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxTypeMappingGenerator"/> class.
    /// </summary>
    public OutboxTypeMappingGenerator()
    {
    }

    private const string OutboxMessageAttr = "EricksonLopez.Outbox.Contracts.OutboxMessageAttribute";

    // OUTBOXSG001 / OUTBOXSG002 use the SG prefix to distinguish from the Analyzer rules
    // (OUTBOX001–OUTBOX006) which exist in EricksonLopez.Outbox.Analyzers.
    // Mixing IDs across projects causes IDE ambiguity and RS2008 tracking conflicts.
    private static readonly DiagnosticDescriptor DuplicateAliasDiagnostic = new(
        id: "OUTBOXSG001",
        title: "Duplicate Outbox Message Alias",
        messageFormat: "The alias '{0}' is already used by type '{1}'. Aliases must be unique.",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor GenericTypeDiagnostic = new(
        id: "OUTBOXSG002",
        title: "Invalid Outbox Message Type",
        messageFormat: "The type '{0}' is a generic type and cannot be used as an outbox message directly. Use non-generic types for messages.",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // ISSUE-SG3 FIX: Emitted when an assembly references EricksonLopez.Outbox but has
    // no types annotated with [OutboxMessage]. Without this warning the user would get
    // a confusing runtime exception when first calling IOutbox.StoreAsync<T>().
    //
    // Severity policy: Warning (not Error).
    //
    // Rationale for Warning (not Error):
    //   An Error breaks compilation for legitimate scenarios:
    //     - Shared contracts library that defines POCO types but has no DI setup
    //     - Test project that imports the package only for IOutboxTester / FakeOutboxRepository
    //     - Transitive dependency (assembly that references the package but doesn't use [OutboxMessage])
    //   In all these cases, a compile error is hostile. Warning is visible but non-breaking.
    //
    // Rationale for not suppressing entirely:
    //   An assembly that references EricksonLopez.Outbox but contains no [OutboxMessage] types
    //   will generate a resolver that resolves nothing. Calling IOutbox.StoreAsync<T>() with
    //   an unregistered T produces a runtime InvalidOperationException silently after deployment.
    //   The warning makes this potential mistake visible at build time.
    //
    // To escalate to Error in your project, add to .editorconfig:
    //   dotnet_diagnostic.OUTBOXSG003.severity = error
    //
    // To suppress entirely (when intentional), add to your .csproj:
    //   <NoWarn>OUTBOXSG003</NoWarn>
    //
    // To suppress via code (when manual registration is used):
    //   options.UseTypeResolver(myResolver);  // in DI setup
    private static readonly DiagnosticDescriptor NoMessageTypesDiagnostic = new(
        id: "OUTBOXSG003",
        title: "No Outbox Message Types Found",
        messageFormat: "No types decorated with [OutboxMessage] were found in this assembly. "
            + "The source-generated IOutboxMessageTypeResolver will not be able to resolve any message types, "
            + "which will cause runtime failures when IOutbox.StoreAsync<T>() is called with any type T. "
            + "To fix: annotate at least one message type with [OutboxMessage(\"your.alias\")]. "
            + "To suppress: call options.UseTypeResolver() for manual registration, or add <NoWarn>OUTBOXSG003</NoWarn> if this assembly intentionally has no outbox messages. "
            + "To escalate to an error: add 'dotnet_diagnostic.OUTBOXSG003.severity = error' to .editorconfig.",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Initializes the generator.
    /// </summary>
    /// <param name="context">The initialization context.</param>
    /// <remarks>
    /// <para>
    /// <b>Hot Reload / Edit-and-Continue (EnC) compatibility:</b>
    /// This generator is deterministic: given the same input types, it always produces
    /// identical output. Each source output file is keyed by a stable name
    /// (<c>OutboxRegistrationExtensions.g.cs</c>, <c>OutboxJsonContext.g.cs</c>),
    /// so the Roslyn incremental driver can diff and skip unchanged outputs efficiently.
    /// This avoids the "spurious re-generation" problem that causes IDE lag during EnC sessions.
    /// </para>
    /// </remarks>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // ISSUE-SG1 (P3 TODO): Future v2.0 feature — generate PostgreSQL/SQL Server CREATE TABLE scripts
        // from [OutboxMessage] attribute metadata so users don't need to run 01_Init_Outbox.sql manually.
        // Planned output: OutboxTableCreation.g.sql per [OutboxMessage] type, containing:
        //   - CREATE TABLE IF NOT EXISTS for per-type partitioned tables
        //   - Indexes on (state, deliver_at, created_at)
        //   - fillfactor=70, autovacuum settings
        //   - LISTEN/NOTIFY trigger
        // This would be gated behind a [GenerateOutboxSchema] assembly attribute to opt-in.
        //
        // Deferred because:
        //   1. SQL output is engine-specific (PostgreSQL vs SQL Server vs SQLite differ substantially)
        //   2. The Roslyn additional files mechanism needs careful design to avoid conflicts with EF Core migrations
        //   3. The existing Scripts/01_Init_Outbox.sql is well-maintained and sufficient for v1.0

        var messageTypes = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                OutboxMessageAttr,
                predicate: (_, _) => true,
                transform: (ctx, _) =>
                {
                    var symbol = (INamedTypeSymbol)ctx.TargetSymbol;
                    var attr = ctx.Attributes.First();
                    var alias = attr.ConstructorArguments.Length > 0
                        ? attr.ConstructorArguments[0].Value?.ToString() ?? symbol.Name
                        : symbol.Name;

                    return new MessageTypeInfo(
                        FullName: GetRuntimeFullName(symbol),
                        Alias: alias,
                        ShortName: symbol.Name,
                        IsGenericType: symbol.IsGenericType,
                        IsUnboundGenericType: symbol.IsUnboundGenericType,
                        Location: symbol.Locations.FirstOrDefault());
                })
            .Where(static x => x is not null)
            .Collect()!;

        var compilationProvider = context.CompilationProvider;
        var combined = messageTypes.Combine(compilationProvider);

        context.RegisterSourceOutput(combined, static (spc, source) => GenerateCode(spc, source.Left, source.Right));
    }


    private static void GenerateCode(
        SourceProductionContext spc,
        ImmutableArray<MessageTypeInfo> types,
        Compilation compilation)
    {
        if (types.IsEmpty)
        {
            // ISSUE-SG3 FIX: Only emit OUTBOXSG003 if this assembly actually references the
            // outbox core library. Without this guard, source generators run for ALL projects
            // in the solution, including test projects that don't use [OutboxMessage] at all.
            //
            // We check for the OutboxMessageAttribute type because:
            //   1. It is in EricksonLopez.Outbox.Contracts, referenced by all users.
            //   2. If it is resolvable, the outbox package is present and the user
            //      probably forgot to annotate their message types.
            var outboxAttrSymbol = compilation.GetTypeByMetadataName(OutboxMessageAttr);
            if (outboxAttrSymbol != null)
            {
                // A-04 AUDIT FIX: Attempt to provide a meaningful diagnostic location instead of
                // Location.None. A navigable location gives the user a clickable "go to" link
                // in the IDE error list, pointing them toward where to add [OutboxMessage].
                //
                // Strategy: Find the first non-generated SyntaxTree in the compilation and use
                // the beginning of its root as the location. This points the diagnostic to the
                // first source file in the project (typically the entry point or a primary type),
                // which is better than "no location".
                //
                // Fallback: Location.None if no suitable syntax tree is found (e.g., source-only projects).
                Location diagnosticLocation = Location.None;
                foreach (var tree in compilation.SyntaxTrees)
                {
                    // Skip generated files — they're not actionable for the user.
                    if (tree.FilePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
                        tree.FilePath.EndsWith(".Generated.cs", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var root = tree.GetRoot();
                    diagnosticLocation = root.GetLocation();
                    break;
                }

                spc.ReportDiagnostic(Diagnostic.Create(NoMessageTypesDiagnostic, diagnosticLocation));
            }
            return;
        }

        var validTypes = new System.Collections.Generic.List<MessageTypeInfo>();
        var aliasMap = new System.Collections.Generic.Dictionary<string, MessageTypeInfo>(StringComparer.Ordinal);

        foreach (var t in types.Distinct())
        {
            if (t.IsGenericType)
            {
                spc.ReportDiagnostic(Diagnostic.Create(GenericTypeDiagnostic, t.Location, t.FullName));
                continue;
            }

            if (aliasMap.TryGetValue(t.Alias, out var existing))
            {
                if (existing.FullName != t.FullName)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(DuplicateAliasDiagnostic, t.Location, t.Alias, existing.FullName));
                }
                continue;
            }

            aliasMap[t.Alias] = t;
            validTypes.Add(t);
        }

        // Emit two separate generated files:
        //   1. OutboxRegistrationExtensions.g.cs — type resolver + UseGeneratedTypes() DI extension
        //   2. OutboxJsonContext.g.cs            — partial JsonSerializerContext with [JsonSerializable] per type
        var safeName = compilation.AssemblyName?.Replace(".", "") ?? "Outbox";
        // P1-FIX: string.GetHashCode() uses a randomized seed in .NET (non-deterministic across builds).
        // Using a deterministic polynomial hash (DJB2-style) ensures the generated class name is
        // stable across compilation sessions. Without this, the Roslyn incremental generator
        // invalidates its cache on every build because the output file name changes.
        var hash = GetDeterministicHash(compilation.AssemblyName ?? string.Empty);
        var assemblyName = $"{safeName}{hash}";
        var contextName = $"{assemblyName}GeneratedJsonContext";

        GenerateRegistrationExtensions(spc, validTypes, contextName);
        GenerateJsonSerializerContext(spc, validTypes, contextName);
    }

    private static string GetRuntimeFullName(INamedTypeSymbol symbol)
    {
        if (symbol.ContainingType == null)
            return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "");

        return GetRuntimeFullName(symbol.ContainingType) + "+" + symbol.Name;
    }

    /// <summary>
    /// Computes a deterministic hash of the given string using a DJB2-style polynomial.
    /// This avoids the non-deterministic behaviour of <see cref="string.GetHashCode()"/> which uses
    /// a randomized seed in .NET, causing different values across different build processes.
    /// The output is always non-negative.
    /// </summary>
    private static int GetDeterministicHash(string value)
    {
        unchecked
        {
            int hash = 17;
            foreach (char c in value)
                hash = hash * 31 + c;
            return Math.Abs(hash);
        }
    }

    private static void GenerateRegistrationExtensions(
        SourceProductionContext spc,
        System.Collections.Generic.List<MessageTypeInfo> types,
        string contextName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// Generated by EricksonLopez.Outbox.SourceGenerators");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Collections.Frozen;");
        sb.AppendLine("using System.Collections.Immutable;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection.Extensions;");
        sb.AppendLine("using System.Text.Json.Serialization;");
        sb.AppendLine("using EricksonLopez.Outbox.Serialization;");
        sb.AppendLine();
        sb.AppendLine("namespace EricksonLopez.Outbox.Generated;");
        sb.AppendLine();

        // ── GeneratedMessageTypeResolver ──────────────────────────────────────
        sb.AppendLine("public sealed class GeneratedMessageTypeResolver : IOutboxMessageTypeResolver");
        sb.AppendLine("{");
        // FIX-09: Use FrozenDictionary<string,Type> instead of ImmutableDictionary.
        // FrozenDictionary is designed for read-heavy, write-once workloads (set once at startup,
        // read millions of times per second). Benchmarks show ~30% better throughput vs ImmutableDictionary
        // for string-key lookups due to more aggressive static hash optimization.
        // It requires .NET 8+ which is already a baseline requirement of this library.
        sb.AppendLine("    private static readonly IReadOnlyDictionary<string, Type> _mappings;");
        sb.AppendLine("    static GeneratedMessageTypeResolver()");
        sb.AppendLine("    {");
        sb.AppendLine("        // FrozenDictionary: build from temp dict, freeze at class init (done once).");
        sb.AppendLine("        var builder = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);");
        foreach (var t in types)
        {
            sb.AppendLine($"        builder[\"{t.Alias}\"] = typeof({t.FullName});");
        }
        sb.AppendLine("        _mappings = builder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public Type? Resolve(string alias)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (_mappings.TryGetValue(alias, out var type)) return type;");
        sb.AppendLine("        return null;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public bool TryGetAlias(Type messageType, out string? alias)");
        sb.AppendLine("    {");
        sb.AppendLine("        alias = null;");
        sb.AppendLine("        var name = messageType.FullName;");
        sb.AppendLine("        if (name == null) return false;");
        // switch is O(1) via jump table — no reflection, no dictionary lookup
        sb.AppendLine("        switch (name)");
        sb.AppendLine("        {");
        foreach (var t in types)
        {
            sb.AppendLine($"            case \"{t.FullName}\": alias = \"{t.Alias}\"; return true;");
        }
        sb.AppendLine("            default: return false;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public string GetAlias(Type messageType)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (TryGetAlias(messageType, out var alias)) return alias!;");
        sb.AppendLine("        throw new InvalidOperationException($\"Type '{messageType.FullName}' is not registered.\");");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public IReadOnlyDictionary<string, Type> GetAllMappings() => _mappings;");
        sb.AppendLine("}");
        sb.AppendLine();
        // ── OutboxRegistrationExtensions ──────────────────────────────────────
        // OutboxRegistrationExtensions is declared as `partial` for future extensibility;
        // all overloads (no-arg and with JsonSerializerContext) live in this single generated file.
        sb.AppendLine("public static partial class OutboxRegistrationExtensions");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Registers the source-generated <see cref=\"IOutboxMessageTypeResolver\"/> (alias\u2192Type resolver).");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    /// <remarks>");
        sb.AppendLine("    /// For NativeAOT serialization, also call");
        sb.AppendLine("    /// <see cref=\"UseGeneratedTypes(global::EricksonLopez.Outbox.OutboxOptions, global::System.Text.Json.Serialization.JsonSerializerContext)\"/>");
        sb.AppendLine("    /// passing your <c>JsonSerializerContext</c> decorated with");
        sb.AppendLine("    /// <c>[JsonSerializable(typeof(YourMessageType))]</c>.");
        sb.AppendLine("    /// See <c>OutboxJsonContext.g.cs</c> in your obj/ folder for the exact template to use.");
        sb.AppendLine("    /// </remarks>");
        // Users call: services.AddOutbox(options => options.UseGeneratedTypes())
        sb.AppendLine("    public static global::EricksonLopez.Outbox.OutboxOptions UseGeneratedTypes(");
        sb.AppendLine("        this global::EricksonLopez.Outbox.OutboxOptions options)");
        sb.AppendLine("    {");
        sb.AppendLine("        // TryAddSingleton: respects any IOutboxMessageTypeResolver registered prior to this call.");
        sb.AppendLine("        options.Configure(services => services.TryAddSingleton<IOutboxMessageTypeResolver, GeneratedMessageTypeResolver>());");
        sb.AppendLine("        return options;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Registers the source-generated <see cref=\"IOutboxMessageTypeResolver\"/> and configures");
        sb.AppendLine("    /// the strict NativeAOT JSON serializer using the provided <paramref name=\"jsonContext\"/>.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    /// <remarks>");
        sb.AppendLine("    /// <para>");
        sb.AppendLine("    /// The provided <paramref name=\"jsonContext\"/> must be a");
        sb.AppendLine("    /// <see cref=\"global::System.Text.Json.Serialization.JsonSerializerContext\"/> generated by the");
        sb.AppendLine("    /// System.Text.Json source generator, decorated with");
        sb.AppendLine("    /// <c>[JsonSerializable(typeof(YourMessageType))]</c> for every");
        sb.AppendLine("    /// type annotated with <c>[OutboxMessage]</c> in your assembly.");
        sb.AppendLine("    /// </para>");
        sb.AppendLine("    /// <para>");
        sb.AppendLine("    /// See <c>OutboxJsonContext.g.cs</c> in your <c>obj/</c> folder for the exact");
        sb.AppendLine("    /// copy-pasteable template.");
        sb.AppendLine("    /// </para>");
        sb.AppendLine("    /// </remarks>");
        sb.AppendLine("    public static global::EricksonLopez.Outbox.OutboxOptions UseGeneratedTypes(");
        sb.AppendLine("        this global::EricksonLopez.Outbox.OutboxOptions options,");
        sb.AppendLine("        global::System.Text.Json.Serialization.JsonSerializerContext jsonContext)");
        sb.AppendLine("    {");
        sb.AppendLine("        // P1-1 AUDIT FIX: Validate that all [OutboxMessage] types have a matching");
        sb.AppendLine("        // [JsonSerializable] entry in the user's JsonSerializerContext.");
        sb.AppendLine("        // This catches the #1 user mistake at startup instead of at runtime.");
        sb.AppendLine("        ValidateJsonSerializerContext(jsonContext);");
        sb.AppendLine("        options.UseGeneratedTypes();");
        sb.AppendLine("        options.UseSerializer(new global::EricksonLopez.Outbox.Serialization.NativeAotJsonSerializer(jsonContext));");
        sb.AppendLine("        return options;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Returns the complete list of message type aliases registered by this source-generated resolver.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    /// <remarks>");
        sb.AppendLine("    /// Intended for startup validation: call this method during application startup to");
        sb.AppendLine("    /// enumerate all registered aliases and verify they match your <c>JsonSerializerContext</c>.");
        sb.AppendLine("    /// <example>");
        sb.AppendLine("    /// <code>");
        sb.AppendLine("    /// var aliases = OutboxRegistrationExtensions.GetRegisteredAliases();");
        sb.AppendLine("    /// // aliases contains all [OutboxMessage] aliases discovered at compile time.");
        sb.AppendLine("    /// </code>");
        sb.AppendLine("    /// </example>");
        sb.AppendLine("    /// </remarks>");
        sb.AppendLine("    /// <returns>A read-only collection of all registered message type aliases.</returns>");
        sb.AppendLine("    public static global::System.Collections.Generic.IReadOnlyList<string> GetRegisteredAliases()");
        sb.AppendLine("    {");
        // A-03 AUDIT FIX: Use string.Join to avoid trailing comma on last element.
        // Previous implementation: sb.Append($"\"{t.Alias}\", ") per element produced
        // 'new string[] { "a", "b", }' with a trailing comma. While valid C#, it generates
        // cosmetically inconsistent code and may trigger linters in downstream projects.
        sb.Append("        return new string[] { ");
        sb.Append(string.Join(", ", System.Linq.Enumerable.Select(types, t => $"\"{t.Alias}\"")));
        sb.AppendLine(" };");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Returns the complete list of CLR types registered by this source-generated resolver.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    /// <remarks>");
        sb.AppendLine("    /// Use this during startup to verify that all registered types are also");
        sb.AppendLine("    /// included in your <c>JsonSerializerContext</c> for NativeAOT compatibility.");
        sb.AppendLine("    /// </remarks>");
        sb.AppendLine("    /// <returns>A read-only collection of all registered message CLR types.</returns>");
        sb.AppendLine("    public static global::System.Collections.Generic.IReadOnlyList<global::System.Type> GetRegisteredTypes()");
        sb.AppendLine("    {");
        // A-03 AUDIT FIX: Same trailing comma fix as GetRegisteredAliases.
        sb.Append("        return new global::System.Type[] { ");
        sb.Append(string.Join(", ", System.Linq.Enumerable.Select(types, t => $"typeof({t.FullName})")));
        sb.AppendLine(" };");
        sb.AppendLine("    }");
        sb.AppendLine();
        // ── ValidateJsonSerializerContext ──────────────────────────────────────
        // P1-1 AUDIT FIX: Auto-generated startup validation that verifies all
        // [OutboxMessage] types have a matching JsonTypeInfo in the user's
        // JsonSerializerContext. This catches the #1 user mistake: adding a new
        // [OutboxMessage] type but forgetting [JsonSerializable(typeof(T))] in
        // the context — which only manifests as a runtime NullReferenceException.
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Validates that the provided <paramref name=\"jsonContext\"/> contains");
        sb.AppendLine("    /// <c>JsonTypeInfo</c> for every <c>[OutboxMessage]</c>-decorated type discovered at compile time.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    /// <remarks>");
        sb.AppendLine("    /// <para>");
        sb.AppendLine("    /// Call this method at application startup (e.g., in <c>Program.cs</c> or a hosted service)");
        sb.AppendLine("    /// to fail fast if any message type is missing from the <c>JsonSerializerContext</c>.");
        sb.AppendLine("    /// </para>");
        sb.AppendLine("    /// <para>");
        sb.AppendLine("    /// Without this check, a missing <c>[JsonSerializable]</c> attribute will cause a");
        sb.AppendLine("    /// <c>NullReferenceException</c> or <c>NotSupportedException</c> at runtime when the outbox");
        sb.AppendLine("    /// attempts to serialize the unregistered type — typically only in production under load.");
        sb.AppendLine("    /// </para>");
        sb.AppendLine("    /// <example>");
        sb.AppendLine("    /// <code>");
        sb.AppendLine("    /// // In Program.cs or a hosted service:");
        sb.AppendLine("    /// OutboxRegistrationExtensions.ValidateJsonSerializerContext(MyJsonContext.Default);");
        sb.AppendLine("    /// </code>");
        sb.AppendLine("    /// </example>");
        sb.AppendLine("    /// </remarks>");
        sb.AppendLine("    /// <param name=\"jsonContext\">The <see cref=\"global::System.Text.Json.Serialization.JsonSerializerContext\"/> to validate against.</param>");
        sb.AppendLine("    /// <exception cref=\"global::System.InvalidOperationException\">");
        sb.AppendLine("    /// Thrown when one or more <c>[OutboxMessage]</c> types are not registered in the context.");
        sb.AppendLine("    /// The exception message lists all missing types for easy remediation.");
        sb.AppendLine("    /// </exception>");
        sb.AppendLine("    public static void ValidateJsonSerializerContext(");
        sb.AppendLine("        global::System.Text.Json.Serialization.JsonSerializerContext jsonContext)");
        sb.AppendLine("    {");
        sb.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(jsonContext);");
        sb.AppendLine("        var missingTypes = new global::System.Collections.Generic.List<string>();");
        sb.AppendLine("        var registeredTypes = GetRegisteredTypes();");
        sb.AppendLine("        for (int i = 0; i < registeredTypes.Count; i++)");
        sb.AppendLine("        {");
        sb.AppendLine("            var type = registeredTypes[i];");
        sb.AppendLine("            if (jsonContext.GetTypeInfo(type) == null)");
        sb.AppendLine("            {");
        sb.AppendLine("                missingTypes.Add(type.FullName ?? type.Name);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("        if (missingTypes.Count > 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            throw new global::System.InvalidOperationException(");
        sb.AppendLine("                $\"The following [OutboxMessage] types are missing [JsonSerializable] in your JsonSerializerContext: \" +");
        sb.AppendLine("                $\"{string.Join(\", \", missingTypes)}. \" +");
        sb.AppendLine("                $\"Add [JsonSerializable(typeof(T))] for each missing type to your JsonSerializerContext class. \" +");
        sb.AppendLine("                $\"See the generated template in obj/OutboxJsonContext.g.cs for the exact attributes to copy.\");");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();

        spc.AddSource("OutboxRegistrationExtensions.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static void GenerateJsonSerializerContext(
        SourceProductionContext spc,
        System.Collections.Generic.List<MessageTypeInfo> types,
        string contextName)
    {
        // STJ GENERATOR LIMITATION — Why we cannot emit a working JsonSerializerContext:
        //
        // The System.Text.Json (STJ) source generator processes [JsonSerializable] attributes
        // DURING the same compilation pass as other source generators. However, STJ cannot
        // inspect files that were *emitted* by another generator in the same pass — it only
        // processes hand-authored source files.
        //
        // Emitting a `partial class Foo : JsonSerializerContext { }` from this generator would
        // produce a class whose abstract members (GetTypeInfo, GeneratedSerializerOptions) are
        // never implemented, causing CS0534 compile errors.
        //
        // Solution: emit a copy-pasteable template as a comment. Users copy the template into
        // their project's hand-authored code (any .cs file), and the STJ generator processes
        // it correctly in the next compilation pass.
        //
        // The template is updated automatically when [OutboxMessage] types are added or removed.
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("// Generated by EricksonLopez.Outbox.SourceGenerators");
        sb.AppendLine("//");
        sb.AppendLine("// STJ SOURCE GENERATOR LIMITATION (Roslyn design constraint):");
        sb.AppendLine("// The System.Text.Json source generator cannot process files emitted by other");
        sb.AppendLine("// source generators in the same compilation pass. This is a known Roslyn");
        sb.AppendLine("// constraint (https://github.com/dotnet/roslyn/issues/57239) with no current workaround.");
        sb.AppendLine("//");
        sb.AppendLine("// ACTION REQUIRED — COPY THIS TEMPLATE INTO YOUR PROJECT:");
        sb.AppendLine("// 1. Create a new file (e.g., OutboxJsonContext.cs) in your project.");
        sb.AppendLine("// 2. Copy the content of the template below (between the /* and */) into that file.");
        sb.AppendLine("// 3. Replace 'Your.Namespace.Here' with your actual project namespace.");
        sb.AppendLine("// 4. In your DI setup, call: options.UseGeneratedTypes(OutboxGeneratedJsonContext.Default);");
        sb.AppendLine("//");
        sb.AppendLine("// This template is kept up-to-date: when you add or remove [OutboxMessage] types,");
        sb.AppendLine("// rebuild your project and re-copy the updated template.");
        sb.AppendLine("/*");
        sb.AppendLine("using System.Text.Json.Serialization;");
        sb.AppendLine();
        sb.AppendLine("namespace Your.Namespace.Here;");
        sb.AppendLine();
        sb.AppendLine("[JsonSourceGenerationOptions(");
        sb.AppendLine("    WriteIndented = false,");
        sb.AppendLine("    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,");
        sb.AppendLine("    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]");
        foreach (var t in types)
        {
            sb.AppendLine($"[JsonSerializable(typeof(global::{t.FullName}))]");
        }
        sb.AppendLine($"public partial class OutboxGeneratedJsonContext : JsonSerializerContext {{ }}");
        sb.AppendLine("*/");
        sb.AppendLine();

        spc.AddSource("OutboxJsonContext.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private sealed record MessageTypeInfo(string FullName, string Alias, string ShortName, bool IsGenericType, bool IsUnboundGenericType, Location? Location);
}
