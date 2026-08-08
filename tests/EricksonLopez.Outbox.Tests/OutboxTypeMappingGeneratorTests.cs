using System;
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
using System;
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

        diagnostics.Should().ContainSingle(d => d.Id == "OUTBOXSG001");
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

        diagnostics.Should().ContainSingle(d => d.Id == "OUTBOXSG002");
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

        diagnostics.Should().ContainSingle(d => d.Id == "OUTBOXSG003");
    }
}

