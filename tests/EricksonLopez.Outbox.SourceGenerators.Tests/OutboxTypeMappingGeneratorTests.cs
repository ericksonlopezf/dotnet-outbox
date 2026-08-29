// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using EricksonLopez.Outbox.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace EricksonLopez.Outbox.Tests.SourceGenerators;

public class OutboxTypeMappingGeneratorTests
{
    [Fact]
    public void Generator_Should_Generate_RegistrationExtensions()
    {
        var source = @"

using System;
namespace EricksonLopez.Outbox.Contracts
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class OutboxMessageAttribute : Attribute
    {
        public string Alias { get; }
        public OutboxMessageAttribute(string alias) { Alias = alias; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class InboxConsumerAttribute : Attribute
    {
        public string EventAlias { get; }
        public InboxConsumerAttribute(string eventAlias) { EventAlias = eventAlias; }
    }
}

namespace TestNamespace
{
    [EricksonLopez.Outbox.Contracts.OutboxMessage(""test.alias"")]
    public class TestMessage { }

    public class Consumer
    {
        [EricksonLopez.Outbox.Contracts.InboxConsumer(""test.alias"")]
        public void Handle() { }
    }
}";

        var compilation = CSharpCompilation.Create("TestProj")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source))
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var generator = new OutboxTypeMappingGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();

        diagnostics.Should().BeEmpty();
        // Generator now emits TWO files:
        //   1. OutboxRegistrationExtensions.g.cs — resolver + UseGeneratedTypes() DI extensions
        //   2. OutboxJsonContext.g.cs             — JsonSerializerContext template + UseGeneratedTypes(context) overload
        runResult.GeneratedTrees.Length.Should().Be(2);

        var extCode = runResult.GeneratedTrees.FirstOrDefault(t => t.FilePath.EndsWith("OutboxRegistrationExtensions.g.cs", StringComparison.Ordinal))?.GetText().ToString();
        extCode.Should().NotBeNull();
        extCode!.Contains("builder[\"test.alias\"] = typeof(TestNamespace.TestMessage);").Should().BeTrue();

        var jsonCtxCode = runResult.GeneratedTrees.FirstOrDefault(t => t.FilePath.EndsWith("OutboxJsonContext.g.cs", StringComparison.Ordinal))?.GetText().ToString();
        jsonCtxCode.Should().NotBeNull();
        // Verify the template comment includes the correct [JsonSerializable] attribute for the registered type
        jsonCtxCode!.Contains("typeof(global::TestNamespace.TestMessage)").Should().BeTrue();
        // Verify the UseGeneratedTypes(JsonSerializerContext) overload is generated
        extCode.Contains("UseGeneratedTypes").Should().BeTrue();
    }
    [Fact]
    public void Generator_Should_Not_Generate_When_No_Attributes()
    {
        var source = @"
namespace TestNamespace
{
    public class TestMessage { }
}";

        var compilation = CSharpCompilation.Create("TestProj")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source))
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var generator = new OutboxTypeMappingGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();

        diagnostics.Should().BeEmpty();
        runResult.GeneratedTrees.Length.Should().Be(0);
    }

    [Fact]
    public void Generator_Should_Report_DuplicateAlias_When_Aliases_Conflict()
    {
        var source = @"
using System;
namespace EricksonLopez.Outbox.Contracts { public sealed class OutboxMessageAttribute : Attribute { public OutboxMessageAttribute(string alias) {} } }
namespace TestNamespace
{
    [EricksonLopez.Outbox.Contracts.OutboxMessage(""conflict.alias"")]
    public class Message1 { }

    [EricksonLopez.Outbox.Contracts.OutboxMessage(""conflict.alias"")]
    public class Message2 { }
}";

        var compilation = CSharpCompilation.Create("TestProj")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source))
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var generator = new OutboxTypeMappingGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        var diag = diagnostics.Should().ContainSingle(d => d.Id == "OUTBOXSG001").Which;
        diag.Severity.Should().Be(DiagnosticSeverity.Error);
        diag.Descriptor.Title.ToString().Should().Be("Duplicate Outbox Message Alias");
        diag.Descriptor.Category.Should().Be("Design");
        diag.Descriptor.MessageFormat.ToString().Should().Be("The alias '{0}' is already used by type '{1}'. Aliases must be unique.");
        diag.GetMessage().Should().Be("The alias 'conflict.alias' is already used by type 'TestNamespace.Message1'. Aliases must be unique.");
    }

    [Fact]
    public void Generator_Should_Report_GenericType_When_Type_Is_Generic()
    {
        var source = @"
using System;
namespace EricksonLopez.Outbox.Contracts { public sealed class OutboxMessageAttribute : Attribute { public OutboxMessageAttribute(string alias) {} } }
namespace TestNamespace
{
    [EricksonLopez.Outbox.Contracts.OutboxMessage(""generic.msg"")]
    public class GenericMessage<T> { }
}";

        var compilation = CSharpCompilation.Create("TestProj")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source))
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var generator = new OutboxTypeMappingGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        var diag = diagnostics.Should().ContainSingle(d => d.Id == "OUTBOXSG002").Which;
        diag.Severity.Should().Be(DiagnosticSeverity.Error);
        diag.Descriptor.Title.ToString().Should().Be("Invalid Outbox Message Type");
        diag.Descriptor.Category.Should().Be("Design");
        diag.Descriptor.MessageFormat.ToString().Should().Be("The type '{0}' is a generic type and cannot be used as an outbox message directly. Use non-generic types for messages.");
        diag.GetMessage().Should().Be("The type 'TestNamespace.GenericMessage<T>' is a generic type and cannot be used as an outbox message directly. Use non-generic types for messages.");
    }

    [Fact]
    public void Generator_Should_Report_NoMessageTypes_When_Only_Referencing_Library()
    {
        var source = @"
using System;
namespace EricksonLopez.Outbox.Contracts { public sealed class OutboxMessageAttribute : Attribute { public OutboxMessageAttribute(string alias) {} } }
namespace TestNamespace
{
    public class NotAMessage { }
}";

        var compilation = CSharpCompilation.Create("TestProj")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source))
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var generator = new OutboxTypeMappingGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        var diag = diagnostics.Should().ContainSingle(d => d.Id == "OUTBOXSG003").Which;
        diag.Severity.Should().Be(DiagnosticSeverity.Warning);
        diag.Descriptor.Title.ToString().Should().Be("No Outbox Message Types Found");
        diag.Descriptor.Category.Should().Be("Design");
        var expectedFormat = "No types decorated with [OutboxMessage] were found in this assembly. "
            + "The source-generated IOutboxMessageTypeResolver will not be able to resolve any message types, "
            + "which will cause runtime failures when IOutbox.StoreAsync<T>() is called with any type T. "
            + "To fix: annotate at least one message type with [OutboxMessage(\"your.alias\")]. "
            + "To suppress: call options.UseTypeResolver() for manual registration, or add <NoWarn>OUTBOXSG003</NoWarn> if this assembly intentionally has no outbox messages. "
            + "To escalate to an error: add 'dotnet_diagnostic.OUTBOXSG003.severity = error' to .editorconfig.";
        diag.Descriptor.MessageFormat.ToString().Should().Be(expectedFormat);
        diag.GetMessage().Should().Be(expectedFormat);
    }

    [Fact]
    public void Generator_Should_Handle_Multiple_Types_And_Generate_Exact_Array_Initializers()
    {
        var source = @"
using System;
namespace EricksonLopez.Outbox.Contracts
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class OutboxMessageAttribute : Attribute
    {
        public string Alias { get; }
        public OutboxMessageAttribute(string alias) { Alias = alias; }
    }
}
namespace TestNamespace
{
    [EricksonLopez.Outbox.Contracts.OutboxMessage(""order.created"")]
    public class OrderCreated { }

    [EricksonLopez.Outbox.Contracts.OutboxMessage(""order.cancelled"")]
    public class OrderCancelled { }
}";

        var compilation = CSharpCompilation.Create("TestProj")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source))
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var generator = new OutboxTypeMappingGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        diagnostics.Should().BeEmpty();
        var runResult = driver.GetRunResult();
        runResult.GeneratedTrees.Length.Should().Be(2);

        var extCode = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("OutboxRegistrationExtensions.g.cs", StringComparison.Ordinal)).GetText().ToString();
        extCode.Should().Contain("return new string[] { \"order.created\", \"order.cancelled\" };");
        extCode.Should().Contain("return new global::System.Type[] { typeof(TestNamespace.OrderCreated), typeof(TestNamespace.OrderCancelled) };");
    }

    [Fact]
    public void Generator_Should_Handle_Nested_Classes()
    {
        var source = @"
using System;
namespace EricksonLopez.Outbox.Contracts { public sealed class OutboxMessageAttribute : Attribute { public OutboxMessageAttribute(string alias) {} } }
namespace TestNamespace
{
    public class OuterClass
    {
        [EricksonLopez.Outbox.Contracts.OutboxMessage(""nested.alias"")]
        public class NestedMessage { }
    }
}";

        var compilation = CSharpCompilation.Create("TestProj")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source))
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var generator = new OutboxTypeMappingGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        diagnostics.Should().BeEmpty();
        var runResult = driver.GetRunResult();
        var extCode = runResult.GeneratedTrees.FirstOrDefault(t => t.FilePath.EndsWith("OutboxRegistrationExtensions.g.cs", StringComparison.Ordinal))?.GetText().ToString();
        extCode.Should().NotBeNull();
        extCode!.Contains("builder[\"nested.alias\"] = typeof(TestNamespace.OuterClass+NestedMessage);").Should().BeTrue();
    }

    [Fact]
    public void Generator_Should_Handle_Default_Alias_When_Constructor_Has_No_Args()
    {
        var source = @"
using System;
namespace EricksonLopez.Outbox.Contracts { public sealed class OutboxMessageAttribute : Attribute { public OutboxMessageAttribute() {} } }
namespace TestNamespace
{
    [EricksonLopez.Outbox.Contracts.OutboxMessage]
    public class MessageWithDefaultAlias { }
}";

        var compilation = CSharpCompilation.Create("TestProj.Sub")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source))
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var generator = new OutboxTypeMappingGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        diagnostics.Should().BeEmpty();
        var runResult = driver.GetRunResult();
        var extCode = runResult.GeneratedTrees.FirstOrDefault(t => t.FilePath.EndsWith("OutboxRegistrationExtensions.g.cs", StringComparison.Ordinal))?.GetText().ToString();
        extCode.Should().NotBeNull();
        extCode!.Contains("builder[\"MessageWithDefaultAlias\"] = typeof(TestNamespace.MessageWithDefaultAlias);").Should().BeTrue();
    }

    [Fact]
    public void Generator_Should_Skip_Generated_Files_When_Locating_Diagnostic()
    {
        var dummyGeneratedSource = "// <auto-generated/>";
        var source = @"
using System;
namespace EricksonLopez.Outbox.Contracts { public sealed class OutboxMessageAttribute : Attribute { public OutboxMessageAttribute(string alias) {} } }
namespace TestNamespace
{
    public class NotAMessage { }
}";

        var compilation = CSharpCompilation.Create("TestProj")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddSyntaxTrees(
                CSharpSyntaxTree.ParseText(dummyGeneratedSource, path: "File.g.cs"),
                CSharpSyntaxTree.ParseText(dummyGeneratedSource, path: "File.Generated.cs"),
                CSharpSyntaxTree.ParseText(source, path: "Normal.cs"))
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var generator = new OutboxTypeMappingGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        diagnostics.Should().ContainSingle(d => d.Id == "OUTBOXSG003");
    }

    [Fact]
    public void Generator_Should_Handle_Null_AssemblyName()
    {
        var source = @"
using System;
namespace EricksonLopez.Outbox.Contracts { public sealed class OutboxMessageAttribute : Attribute { public OutboxMessageAttribute(string alias) {} } }
namespace TestNamespace
{
    [EricksonLopez.Outbox.Contracts.OutboxMessage(""my.alias"")]
    public class MyMsg { }
}";

        var compilation = CSharpCompilation.Create(null)
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source))
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var generator = new OutboxTypeMappingGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Generator_Should_Fallback_To_Symbol_Name_When_Alias_Argument_Is_Null()
    {
        var source = @"
using System;
namespace EricksonLopez.Outbox.Contracts { public sealed class OutboxMessageAttribute : Attribute { public OutboxMessageAttribute(string alias) {} } }
namespace TestNamespace
{
    [EricksonLopez.Outbox.Contracts.OutboxMessage(null)]
    public class MessageWithNullArgAlias { }
}";

        var compilation = CSharpCompilation.Create("TestProj")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source))
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var generator = new OutboxTypeMappingGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        diagnostics.Should().BeEmpty();
        var runResult = driver.GetRunResult();
        var extCode = runResult.GeneratedTrees.FirstOrDefault(t => t.FilePath.EndsWith("OutboxRegistrationExtensions.g.cs", StringComparison.Ordinal))?.GetText().ToString();
        extCode.Should().NotBeNull();
        extCode!.Contains("builder[\"MessageWithNullArgAlias\"] = typeof(TestNamespace.MessageWithNullArgAlias);").Should().BeTrue();
    }

    [Fact]
    public void Generator_Should_Handle_Deeply_Nested_Classes()
    {
        var source = @"
using System;
namespace EricksonLopez.Outbox.Contracts { public sealed class OutboxMessageAttribute : Attribute { public OutboxMessageAttribute(string alias) {} } }
namespace TestNamespace
{
    public class OuterClass
    {
        public class MiddleClass
        {
            [EricksonLopez.Outbox.Contracts.OutboxMessage(""deep.nested"")]
            public class InnerMessage { }
        }
    }
}";

        var compilation = CSharpCompilation.Create("TestProj")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source))
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var generator = new OutboxTypeMappingGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        diagnostics.Should().BeEmpty();
        var runResult = driver.GetRunResult();
        var extCode = runResult.GeneratedTrees.FirstOrDefault(t => t.FilePath.EndsWith("OutboxRegistrationExtensions.g.cs", StringComparison.Ordinal))?.GetText().ToString();
        extCode.Should().NotBeNull();
        extCode!.Contains("builder[\"deep.nested\"] = typeof(TestNamespace.OuterClass+MiddleClass+InnerMessage);").Should().BeTrue();
    }

    [Fact]
    public void Generator_Should_Ignore_Duplicate_When_Same_Type_And_Same_Alias()
    {
        var source = @"
using System;
namespace EricksonLopez.Outbox.Contracts { public sealed class OutboxMessageAttribute : Attribute { public OutboxMessageAttribute(string alias) {} } }
namespace TestNamespace
{
    [EricksonLopez.Outbox.Contracts.OutboxMessage(""same.alias"")]
    public partial class SameClass { }

    [EricksonLopez.Outbox.Contracts.OutboxMessage(""same.alias"")]
    public partial class SameClass { }
}";

        var compilation = CSharpCompilation.Create("TestProj")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source))
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var generator = new OutboxTypeMappingGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        diagnostics.Should().BeEmpty();
        var runResult = driver.GetRunResult();
        runResult.GeneratedTrees.Length.Should().Be(2);
    }

    [Fact]
    public void Generator_Should_Report_OUTBOXSG003_With_LocationNone_When_All_Files_Are_Generated()
    {
        var dummyGeneratedSource = @"
using System;
namespace EricksonLopez.Outbox.Contracts { public sealed class OutboxMessageAttribute : Attribute { public OutboxMessageAttribute(string alias) {} } }
namespace TestNamespace { public class GenClass {} }";

        var compilation = CSharpCompilation.Create("TestProj")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddSyntaxTrees(
                CSharpSyntaxTree.ParseText(dummyGeneratedSource, path: "File.g.cs"),
                CSharpSyntaxTree.ParseText(dummyGeneratedSource, path: "File.Generated.cs"))
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var generator = new OutboxTypeMappingGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        diagnostics.Should().ContainSingle(d => d.Id == "OUTBOXSG003" && d.Location == Location.None);
    }

    [Fact]
    public void Generator_Should_Not_Emit_Diagnostic_When_OutboxMessageAttribute_Not_Referenced_And_No_Types()
    {
        var source = @"
namespace TestNamespace
{
    public class PlainClass { }
}";

        var compilation = CSharpCompilation.Create("TestProj")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source))
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var generator = new OutboxTypeMappingGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        diagnostics.Should().BeEmpty();
        var runResult = driver.GetRunResult();
        runResult.GeneratedTrees.Length.Should().Be(0);
    }

    [Fact]
    public void GetDeterministicHash_Should_Produce_Consistent_NonNegative_Value()
    {
        var h1 = OutboxTypeMappingGenerator.GetDeterministicHash("TestProj");
        var h2 = OutboxTypeMappingGenerator.GetDeterministicHash("TestProj");
        h1.Should().Be(h2);
        (h1 >= 0).Should().BeTrue();

        var emptyHash = OutboxTypeMappingGenerator.GetDeterministicHash("");
        emptyHash.Should().Be(17 & 0x7FFFFFFF);

        var hA = OutboxTypeMappingGenerator.GetDeterministicHash("A");
        hA.Should().Be((17 * 31 + 'A') & 0x7FFFFFFF);
    }

    [Fact]
    public void Generator_Should_Generate_Complete_Valid_Source_Files()
    {
        var source = @"
using System;
namespace EricksonLopez.Outbox.Contracts
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class OutboxMessageAttribute : Attribute
    {
        public string Alias { get; }
        public OutboxMessageAttribute(string alias) { Alias = alias; }
    }
}
namespace TestNamespace
{
    [EricksonLopez.Outbox.Contracts.OutboxMessage(""test.alias"")]
    public class TestMessage { }
}";

        var compilation = CSharpCompilation.Create("TestProj")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source, path: "Test.cs"))
            .AddReferences(
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Frozen.FrozenDictionary).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Immutable.ImmutableArray).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Text.Json.Serialization.JsonSerializerContext).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(EricksonLopez.Outbox.IOutbox).Assembly.Location));

        var generator = new OutboxTypeMappingGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        diagnostics.Should().BeEmpty();
        var runResult = driver.GetRunResult();
        runResult.GeneratedTrees.Length.Should().Be(2);

        var extTree = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("OutboxRegistrationExtensions.g.cs", StringComparison.Ordinal));
        var extCode = extTree.GetText().ToString();

        extCode.Should().Contain("// <auto-generated />");
        extCode.Should().Contain("// Generated by EricksonLopez.Outbox.SourceGenerators");
        extCode.Should().Contain("#nullable enable");
        extCode.Should().Contain("using System;");
        extCode.Should().Contain("using System.Collections.Generic;");
        extCode.Should().Contain("using System.Collections.Frozen;");
        extCode.Should().Contain("using System.Collections.Immutable;");
        extCode.Should().Contain("using Microsoft.Extensions.DependencyInjection;");
        extCode.Should().Contain("using Microsoft.Extensions.DependencyInjection.Extensions;");
        extCode.Should().Contain("using System.Text.Json.Serialization;");
        extCode.Should().Contain("using EricksonLopez.Outbox.Serialization;");
        extCode.Should().Contain("namespace EricksonLopez.Outbox.Generated;");
        extCode.Should().Contain("public sealed class GeneratedMessageTypeResolver : IOutboxMessageTypeResolver");
        extCode.Should().Contain("private static readonly IReadOnlyDictionary<string, Type> _mappings;");
        extCode.Should().Contain("static GeneratedMessageTypeResolver()");
        extCode.Should().Contain("var builder = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);");
        extCode.Should().Contain("builder[\"test.alias\"] = typeof(TestNamespace.TestMessage);");
        extCode.Should().Contain("_mappings = builder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);");
        extCode.Should().Contain("public Type? Resolve(string alias)");
        extCode.Should().Contain("if (_mappings.TryGetValue(alias, out var type)) return type;");
        extCode.Should().Contain("return null;");
        extCode.Should().Contain("public bool TryGetAlias(Type messageType, out string? alias)");
        extCode.Should().Contain("alias = null;");
        extCode.Should().Contain("var name = messageType.FullName;");
        extCode.Should().Contain("if (name == null) return false;");
        extCode.Should().Contain("switch (name)");
        extCode.Should().Contain("case \"TestNamespace.TestMessage\": alias = \"test.alias\"; return true;");
        extCode.Should().Contain("default: return false;");
        extCode.Should().Contain("public string GetAlias(Type messageType)");
        extCode.Should().Contain("if (TryGetAlias(messageType, out var alias)) return alias!;");
        extCode.Should().Contain("throw new InvalidOperationException($\"Type '{messageType.FullName}' is not registered.\");");
        extCode.Should().Contain("public IReadOnlyDictionary<string, Type> GetAllMappings() => _mappings;");
        extCode.Should().Contain("public static partial class OutboxRegistrationExtensions");
        extCode.Should().Contain("UseGeneratedTypes(");
        extCode.Should().Contain("options.Configure(services => services.TryAddSingleton<IOutboxMessageTypeResolver, GeneratedMessageTypeResolver>());");
        extCode.Should().Contain("public static global::System.Collections.Generic.IReadOnlyList<string> GetRegisteredAliases()");
        extCode.Should().Contain("return new string[] { \"test.alias\" };");
        extCode.Should().Contain("public static global::System.Collections.Generic.IReadOnlyList<global::System.Type> GetRegisteredTypes()");
        extCode.Should().Contain("return new global::System.Type[] { typeof(TestNamespace.TestMessage) };");
        extCode.Should().Contain("public static void ValidateJsonSerializerContext(");
        extCode.Should().Contain("global::System.ArgumentNullException.ThrowIfNull(jsonContext);");
        extCode.Should().Contain("var missingTypes = new global::System.Collections.Generic.List<string>();");
        extCode.Should().Contain("var registeredTypes = GetRegisteredTypes();");
        extCode.Should().Contain("for (int i = 0; i < registeredTypes.Count; i++)");
        extCode.Should().Contain("if (jsonContext.GetTypeInfo(type) == null)");
        extCode.Should().Contain("missingTypes.Add(type.FullName ?? type.Name);");
        extCode.Should().Contain("if (missingTypes.Count > 0)");
        extCode.Should().Contain("throw new global::System.InvalidOperationException(");

        var jsonTree = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("OutboxJsonContext.g.cs", StringComparison.Ordinal));
        var jsonCode = jsonTree.GetText().ToString();
        jsonCode.Should().Contain("// STJ SOURCE GENERATOR LIMITATION");
        jsonCode.Should().Contain("[JsonSerializable(typeof(global::TestNamespace.TestMessage))]");
        jsonCode.Should().Contain("public partial class OutboxGeneratedJsonContext : JsonSerializerContext");
    }

    [Fact]
    public void Generator_Should_Point_To_First_NonGenerated_File_When_Reporting_OUTBOXSG003()
    {
        var dummyGen = "// <auto-generated/>";
        var source1 = @"
using System;
namespace EricksonLopez.Outbox.Contracts { public sealed class OutboxMessageAttribute : Attribute { public OutboxMessageAttribute(string alias) {} } }
namespace TestNamespace { public class Class1 {} }";
        var source2 = "namespace TestNamespace { public class Class2 {} }";

        var compilation = CSharpCompilation.Create("TestProj")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddSyntaxTrees(
                CSharpSyntaxTree.ParseText(dummyGen, path: "File.g.cs"),
                CSharpSyntaxTree.ParseText(source1, path: "FirstNonGen.cs"),
                CSharpSyntaxTree.ParseText(source2, path: "SecondNonGen.cs"))
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var generator = new OutboxTypeMappingGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        var diag = diagnostics.Should().ContainSingle(d => d.Id == "OUTBOXSG003").Which;
        diag.Location.GetLineSpan().Path.Should().Be("FirstNonGen.cs");
    }

    [Fact]
    public void Generator_Should_Skip_Generic_Type_And_Continue_Processing_Other_Types()
    {
        var source = @"
using System;
namespace EricksonLopez.Outbox.Contracts { public sealed class OutboxMessageAttribute : Attribute { public OutboxMessageAttribute(string alias) {} } }
namespace TestNamespace
{
    [EricksonLopez.Outbox.Contracts.OutboxMessage(""generic.alias"")]
    public class GenericMessage<T> { }

    [EricksonLopez.Outbox.Contracts.OutboxMessage(""valid.alias"")]
    public class ValidMessage { }
}";

        var compilation = CSharpCompilation.Create("TestProj")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source))
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var generator = new OutboxTypeMappingGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        diagnostics.Should().ContainSingle(d => d.Id == "OUTBOXSG002");
        var runResult = driver.GetRunResult();
        runResult.GeneratedTrees.Length.Should().Be(2);

        var extCode = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("OutboxRegistrationExtensions.g.cs", StringComparison.Ordinal)).GetText().ToString();
        extCode.Should().Contain("builder[\"valid.alias\"] = typeof(TestNamespace.ValidMessage);");
        extCode.Should().NotContain("GenericMessage");
    }

    [Fact]
    public void Generator_Should_Skip_Duplicate_Alias_And_Keep_Only_First()
    {
        var source = @"
using System;
namespace EricksonLopez.Outbox.Contracts { public sealed class OutboxMessageAttribute : Attribute { public OutboxMessageAttribute(string alias) {} } }
namespace TestNamespace
{
    [EricksonLopez.Outbox.Contracts.OutboxMessage(""dup.alias"")]
    public class Message1 { }

    [EricksonLopez.Outbox.Contracts.OutboxMessage(""dup.alias"")]
    public class Message2 { }
}";

        var compilation = CSharpCompilation.Create("TestProj")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source))
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var generator = new OutboxTypeMappingGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        diagnostics.Should().ContainSingle(d => d.Id == "OUTBOXSG001");
        var runResult = driver.GetRunResult();
        runResult.GeneratedTrees.Length.Should().Be(2);

        var extCode = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("OutboxRegistrationExtensions.g.cs", StringComparison.Ordinal)).GetText().ToString();
        extCode.Should().Contain("builder[\"dup.alias\"] = typeof(TestNamespace.Message1);");
        extCode.Should().NotContain("typeof(TestNamespace.Message2)");
    }

    [Fact]
    public void Generator_Should_Generate_Exact_Source_Code()
    {
        var source = @"
using System;
namespace EricksonLopez.Outbox.Contracts
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class OutboxMessageAttribute : Attribute
    {
        public string Alias { get; }
        public OutboxMessageAttribute(string alias) { Alias = alias; }
    }
}
namespace TestNamespace
{
    [EricksonLopez.Outbox.Contracts.OutboxMessage(""test.alias"")]
    public class TestMessage { }
}";

        var compilation = CSharpCompilation.Create("TestProj")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source))
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var generator = new OutboxTypeMappingGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        diagnostics.Should().BeEmpty();
        var runResult = driver.GetRunResult();
        runResult.GeneratedTrees.Length.Should().Be(2);

        var extCode = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("OutboxRegistrationExtensions.g.cs", StringComparison.Ordinal)).GetText().ToString().Replace("\r\n", "\n");
        var jsonCode = runResult.GeneratedTrees.First(t => t.FilePath.EndsWith("OutboxJsonContext.g.cs", StringComparison.Ordinal)).GetText().ToString().Replace("\r\n", "\n");

        var expectedExt = @"// <auto-generated />
// Generated by EricksonLopez.Outbox.SourceGenerators
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json.Serialization;
using EricksonLopez.Outbox.Serialization;

namespace EricksonLopez.Outbox.Generated;

public sealed class GeneratedMessageTypeResolver : IOutboxMessageTypeResolver
{
    private static readonly IReadOnlyDictionary<string, Type> _mappings;
    static GeneratedMessageTypeResolver()
    {
        // FrozenDictionary: build from temp dict, freeze at class init (done once).
        var builder = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        builder[""test.alias""] = typeof(TestNamespace.TestMessage);
        _mappings = builder.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public Type? Resolve(string alias)
    {
        if (_mappings.TryGetValue(alias, out var type)) return type;
        return null;
    }

    public bool TryGetAlias(Type messageType, out string? alias)
    {
        alias = null;
        var name = messageType.FullName;
        if (name == null) return false;
        switch (name)
        {
            case ""TestNamespace.TestMessage"": alias = ""test.alias""; return true;
            default: return false;
        }
    }

    public string GetAlias(Type messageType)
    {
        if (TryGetAlias(messageType, out var alias)) return alias!;
        throw new InvalidOperationException($""Type '{messageType.FullName}' is not registered."");
    }

    public IReadOnlyDictionary<string, Type> GetAllMappings() => _mappings;
}

public static partial class OutboxRegistrationExtensions
{
    /// <summary>
    /// Registers the source-generated <see cref=""IOutboxMessageTypeResolver""/> (alias→Type resolver).
    /// </summary>
    /// <remarks>
    /// For NativeAOT serialization, also call
    /// <see cref=""UseGeneratedTypes(global::EricksonLopez.Outbox.OutboxOptions, global::System.Text.Json.Serialization.JsonSerializerContext)""/>
    /// passing your <c>JsonSerializerContext</c> decorated with
    /// <c>[JsonSerializable(typeof(YourMessageType))]</c>.
    /// See <c>OutboxJsonContext.g.cs</c> in your obj/ folder for the exact template to use.
    /// </remarks>
    public static global::EricksonLopez.Outbox.OutboxOptions UseGeneratedTypes(
        this global::EricksonLopez.Outbox.OutboxOptions options)
    {
        // TryAddSingleton: respects any IOutboxMessageTypeResolver registered prior to this call.
        options.Configure(services => services.TryAddSingleton<IOutboxMessageTypeResolver, GeneratedMessageTypeResolver>());
        return options;
    }

    /// <summary>
    /// Registers the source-generated <see cref=""IOutboxMessageTypeResolver""/> and configures
    /// the strict NativeAOT JSON serializer using the provided <paramref name=""jsonContext""/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The provided <paramref name=""jsonContext""/> must be a
    /// <see cref=""global::System.Text.Json.Serialization.JsonSerializerContext""/> generated by the
    /// System.Text.Json source generator, decorated with
    /// <c>[JsonSerializable(typeof(YourMessageType))]</c> for every
    /// type annotated with <c>[OutboxMessage]</c> in your assembly.
    /// </para>
    /// <para>
    /// See <c>OutboxJsonContext.g.cs</c> in your <c>obj/</c> folder for the exact
    /// copy-pasteable template.
    /// </para>
    /// </remarks>
    public static global::EricksonLopez.Outbox.OutboxOptions UseGeneratedTypes(
        this global::EricksonLopez.Outbox.OutboxOptions options,
        global::System.Text.Json.Serialization.JsonSerializerContext jsonContext)
    {
        // P1-1 AUDIT FIX: Validate that all [OutboxMessage] types have a matching
        // [JsonSerializable] entry in the user's JsonSerializerContext.
        // This catches the #1 user mistake at startup instead of at runtime.
        ValidateJsonSerializerContext(jsonContext);
        options.UseGeneratedTypes();
        options.UseSerializer(new global::EricksonLopez.Outbox.Serialization.NativeAotJsonSerializer(jsonContext));
        return options;
    }

    /// <summary>
    /// Returns the complete list of message type aliases registered by this source-generated resolver.
    /// </summary>
    /// <remarks>
    /// Intended for startup validation: call this method during application startup to
    /// enumerate all registered aliases and verify they match your <c>JsonSerializerContext</c>.
    /// <example>
    /// <code>
    /// var aliases = OutboxRegistrationExtensions.GetRegisteredAliases();
    /// // aliases contains all [OutboxMessage] aliases discovered at compile time.
    /// </code>
    /// </example>
    /// </remarks>
    /// <returns>A read-only collection of all registered message type aliases.</returns>
    public static global::System.Collections.Generic.IReadOnlyList<string> GetRegisteredAliases()
    {
        return new string[] { ""test.alias"" };
    }

    /// <summary>
    /// Returns the complete list of CLR types registered by this source-generated resolver.
    /// </summary>
    /// <remarks>
    /// Use this during startup to verify that all registered types are also
    /// included in your <c>JsonSerializerContext</c> for NativeAOT compatibility.
    /// </remarks>
    /// <returns>A read-only collection of all registered message CLR types.</returns>
    public static global::System.Collections.Generic.IReadOnlyList<global::System.Type> GetRegisteredTypes()
    {
        return new global::System.Type[] { typeof(TestNamespace.TestMessage) };
    }

    /// <summary>
    /// Validates that the provided <paramref name=""jsonContext""/> contains
    /// <c>JsonTypeInfo</c> for every <c>[OutboxMessage]</c>-decorated type discovered at compile time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call this method at application startup (e.g., in <c>Program.cs</c> or a hosted service)
    /// to fail fast if any message type is missing from the <c>JsonSerializerContext</c>.
    /// </para>
    /// <para>
    /// Without this check, a missing <c>[JsonSerializable]</c> attribute will cause a
    /// <c>NullReferenceException</c> or <c>NotSupportedException</c> at runtime when the outbox
    /// attempts to serialize the unregistered type — typically only in production under load.
    /// </para>
    /// <example>
    /// <code>
    /// // In Program.cs or a hosted service:
    /// OutboxRegistrationExtensions.ValidateJsonSerializerContext(MyJsonContext.Default);
    /// </code>
    /// </example>
    /// </remarks>
    /// <param name=""jsonContext"">The <see cref=""global::System.Text.Json.Serialization.JsonSerializerContext""/> to validate against.</param>
    /// <exception cref=""global::System.InvalidOperationException"">
    /// Thrown when one or more <c>[OutboxMessage]</c> types are not registered in the context.
    /// The exception message lists all missing types for easy remediation.
    /// </exception>
    public static void ValidateJsonSerializerContext(
        global::System.Text.Json.Serialization.JsonSerializerContext jsonContext)
    {
        global::System.ArgumentNullException.ThrowIfNull(jsonContext);
        var missingTypes = new global::System.Collections.Generic.List<string>();
        var registeredTypes = GetRegisteredTypes();
        for (int i = 0; i < registeredTypes.Count; i++)
        {
            var type = registeredTypes[i];
            if (jsonContext.GetTypeInfo(type) == null)
            {
                missingTypes.Add(type.FullName ?? type.Name);
            }
        }
        if (missingTypes.Count > 0)
        {
            throw new global::System.InvalidOperationException(
                $""The following [OutboxMessage] types are missing [JsonSerializable] in your JsonSerializerContext: "" +
                $""{string.Join("", "", missingTypes)}. "" +
                $""Add [JsonSerializable(typeof(T))] for each missing type to your JsonSerializerContext class. "" +
                $""See the generated template in obj/OutboxJsonContext.g.cs for the exact attributes to copy."");
        }
    }
}

".Replace("\r\n", "\n");

        var expectedJson = @"// <auto-generated />
// Generated by EricksonLopez.Outbox.SourceGenerators
//
// STJ SOURCE GENERATOR LIMITATION (Roslyn design constraint):
// The System.Text.Json source generator cannot process files emitted by other
// source generators in the same compilation pass. This is a known Roslyn
// constraint (https://github.com/dotnet/roslyn/issues/57239) with no current workaround.
//
// ACTION REQUIRED — COPY THIS TEMPLATE INTO YOUR PROJECT:
// 1. Create a new file (e.g., OutboxJsonContext.cs) in your project.
// 2. Copy the content of the template below (between the /* and */) into that file.
// 3. Replace 'Your.Namespace.Here' with your actual project namespace.
// 4. In your DI setup, call: options.UseGeneratedTypes(OutboxGeneratedJsonContext.Default);
//
// This template is kept up-to-date: when you add or remove [OutboxMessage] types,
// rebuild your project and re-copy the updated template.
/*
using System.Text.Json.Serialization;

namespace Your.Namespace.Here;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(global::TestNamespace.TestMessage))]
public partial class OutboxGeneratedJsonContext : JsonSerializerContext { }
*/

".Replace("\r\n", "\n");

        extCode.Should().Be(expectedExt);
        jsonCode.Should().Be(expectedJson);
    }
}




