using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Analyzers;

public class OutboxMessageAnalyzerTests
{
    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("TestProj", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var document = project.AddDocument("Test.cs", source);

        var compilation = await document.Project.GetCompilationAsync();
        var compilationWithAnalyzers = compilation!.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new OutboxMessageAnalyzer()));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    [Fact]
    public async Task MissingOutboxMessage_Should_Report_OUTBOX002()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        System.Threading.Tasks.ValueTask Publish<T>(T message) where T : notnull;
    }
}
namespace Test {
    public class MyMessage { }
    public class Usage {
        public async System.Threading.Tasks.Task DoWork(EricksonLopez.Outbox.IOutbox outbox) {
            await outbox.Publish(new MyMessage());
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Any(d => d.Id == "OUTBOX002").Should().BeTrue();
    }

    [Fact]
    public async Task MissingIdProperty_Should_Report_OUTBOX001()
    {
        var source = @"
namespace EricksonLopez.Outbox.Contracts { public class OutboxMessageAttribute : System.Attribute {} }
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        System.Threading.Tasks.ValueTask Publish<T>(T message) where T : notnull;
    }
}
namespace Test {
    [EricksonLopez.Outbox.Contracts.OutboxMessage]
    public class MyMessage { }
    public class Usage {
        public async System.Threading.Tasks.Task DoWork(EricksonLopez.Outbox.IOutbox outbox) {
            await outbox.Publish(new MyMessage());
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Any(d => d.Id == "OUTBOX001").Should().BeTrue();
    }

    [Fact]
    public async Task HasIdProperty_Should_Not_Report_OUTBOX001()
    {
        var source = @"
namespace EricksonLopez.Outbox.Contracts { public class OutboxMessageAttribute : System.Attribute {} }
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        System.Threading.Tasks.ValueTask Publish<T>(T message) where T : notnull;
    }
}
namespace Test {
    [EricksonLopez.Outbox.Contracts.OutboxMessage]
    public class MyMessage {
        public System.Guid Id { get; set; }
    }
    public class Usage {
        public async System.Threading.Tasks.Task DoWork(EricksonLopez.Outbox.IOutbox outbox) {
            await outbox.Publish(new MyMessage());
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingInboxConsumer_Should_Report_OUTBOX003()
    {
        var source = @"
namespace MassTransit {
    public interface IConsumer<T> { }
}
namespace Test {
    public class MyHandler : MassTransit.IConsumer<object> { }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Any(d => d.Id == "OUTBOX003").Should().BeTrue();
    }

    [Fact]
    public async Task InvalidRetryPolicy_Should_Report_OUTBOX004()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class OutboxRetryPolicy {
        public OutboxRetryPolicy(int maxAttempts, int ms) { }
    }
}
namespace Test {
    public class Usage {
        public void Configure() {
            var policy = new EricksonLopez.Outbox.OutboxRetryPolicy(-1, 100);
            var policy2 = new EricksonLopez.Outbox.OutboxRetryPolicy(maxAttempts: -1, 100);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX004").Should().Be(2);
    }

    [Fact]
    public async Task MissingSerializerConfig_Should_Report_OUTBOX005()
    {
        var source = @"
namespace Test {
    public class Setup {
        public void Configure() {
            AddOutbox(opts => { });
        }
        public void AddOutbox(System.Action<object> opts) {}
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Any(d => d.Id == "OUTBOX005").Should().BeTrue();
    }

    [Fact]
    public async Task HasSerializerConfig_Should_Not_Report_OUTBOX005()
    {
        var source = @"
namespace Test {
    public class Opts {
        public void UseGeneratedTypes() { }
    }
    public class Setup {
        public void Configure() {
            AddOutbox(opts => {
                opts.UseGeneratedTypes();
                UseSerializer();
            });
        }
        public void UseSerializer() { }
        public void AddOutbox(System.Action<Opts> opts) {}
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Any(d => d.Id == "OUTBOX005").Should().BeFalse();
    }

    [Fact]
    public async Task AbandonedBuilder_Should_Report_OUTBOX008()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class OutboxMessageBuilder<T> {
        public OutboxMessageBuilder<T> WithTransaction(object t) => this;
        public System.Threading.Tasks.ValueTask StoreAsync() => default;
    }
    public interface IOutbox {
        OutboxMessageBuilder<T> Publish<T>(T message) where T : notnull;
    }
}
namespace Test {
    public class MyMessage { }
    public class Usage {
        public void DoWork(EricksonLopez.Outbox.IOutbox outbox) {
            outbox.Publish(new MyMessage()); // Abandoned
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Any(d => d.Id == "OUTBOX008").Should().BeTrue();
    }

    [Fact]
    public async Task CapturedBuilder_Should_Not_Report_OUTBOX008()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class OutboxMessageBuilder<T> {
        public OutboxMessageBuilder<T> WithTransaction(object t) => this;
        public System.Threading.Tasks.ValueTask StoreAsync() => default;
    }
    public interface IOutbox {
        OutboxMessageBuilder<T> Publish<T>(T message) where T : notnull;
    }
}
namespace Test {
    public class MyMessage { }
    public class Usage {
        public async System.Threading.Tasks.Task DoWork(EricksonLopez.Outbox.IOutbox outbox) {
            var builder = outbox.Publish(new MyMessage());
            await builder.StoreAsync();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Any(d => d.Id == "OUTBOX008").Should().BeFalse();
    }

    [Fact]
    public async Task DefaultDispatchResult_Should_Report_OUTBOX012()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IBrokerPublisher {
        System.Threading.Tasks.Task<object> PublishRawAsync();
    }
}
namespace Test {
    public class BadPublisher : EricksonLopez.Outbox.IBrokerPublisher {
        public System.Threading.Tasks.Task<object> PublishRawAsync() {
            return default;
        }
    }
    public class BadPublisher2 : EricksonLopez.Outbox.IBrokerPublisher {
        public System.Threading.Tasks.Task<object> PublishRawAsync() {
            return default(object);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX012").Should().Be(2);
    }

    [Fact]
    public async Task ValidDispatchResult_Should_Not_Report_OUTBOX012()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IBrokerPublisher {
        System.Threading.Tasks.Task<object> PublishRawAsync();
    }
}
namespace Test {
    public class GoodPublisher : EricksonLopez.Outbox.IBrokerPublisher {
        public System.Threading.Tasks.Task<object> PublishRawAsync() {
            return System.Threading.Tasks.Task.FromResult(new object());
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Any(d => d.Id == "OUTBOX012").Should().BeFalse();
    }

    [Fact]
    public async Task MissingJsonSerializable_Should_Report_OUTBOX006()
    {
        var source = @"
namespace EricksonLopez.Outbox.Contracts { public class OutboxMessageAttribute : System.Attribute {} }
namespace System.Text.Json.Serialization {
    public class JsonSerializerContext { }
    public class JsonSerializableAttribute : System.Attribute {
        public JsonSerializableAttribute(System.Type type) { }
    }
}
namespace Test {
    [EricksonLopez.Outbox.Contracts.OutboxMessage]
    public class MyMessage { }
    
    public partial class MyContext : System.Text.Json.Serialization.JsonSerializerContext { }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Any(d => d.Id == "OUTBOX006").Should().BeTrue();
    }

    [Fact]
    public async Task HasJsonSerializable_Should_Not_Report_OUTBOX006()
    {
        var source = @"
namespace EricksonLopez.Outbox.Contracts { public class OutboxMessageAttribute : System.Attribute {} }
namespace System.Text.Json.Serialization {
    public class JsonSerializerContext { }
    public class JsonSerializableAttribute : System.Attribute {
        public JsonSerializableAttribute(System.Type type) { }
    }
}
namespace Test {
    [EricksonLopez.Outbox.Contracts.OutboxMessage]
    public class MyMessage { }
    
    [System.Text.Json.Serialization.JsonSerializable(typeof(MyMessage))]
    public partial class MyContext : System.Text.Json.Serialization.JsonSerializerContext { }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Any(d => d.Id == "OUTBOX006").Should().BeFalse();
    }

    [Fact]
    public async Task NullTransaction_Should_Report_OUTBOX007()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutbox { void StoreAsync<T>(T msg, object transaction = null); }
}
namespace Test {
    public class Usage {
        public void DoWork(EricksonLopez.Outbox.IOutbox outbox) {
            outbox.StoreAsync(new object(), null);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Any(d => d.Id == "OUTBOX007").Should().BeTrue();
    }

    [Fact]
    public async Task ZeroMaxRetries_Should_Report_OUTBOX009()
    {
        var source = @"
namespace Test {
    public class OutboxOptions { public int MaxRetryCount { get; set; } }
    public class Usage {
        public void Configure() {
            var opts = new OutboxOptions();
            opts.MaxRetryCount = 0;
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Any(d => d.Id == "OUTBOX009").Should().BeTrue();
    }

    [Fact]
    public async Task MissingIntegrationEventAlias_Should_Report_OUTBOX011()
    {
        var source = @"
namespace EricksonLopez.Events {
    public interface IIntegrationEvent { }
}
namespace Test {
    public class MyEvent : EricksonLopez.Events.IIntegrationEvent { }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Any(d => d.Id == "OUTBOX011").Should().BeTrue();
    }

    [Fact]
    public async Task HasIntegrationEventAlias_Should_Not_Report_OUTBOX011()
    {
        var source = @"
namespace EricksonLopez.Outbox.Contracts { public class OutboxMessageAttribute : System.Attribute {} }
namespace EricksonLopez.Events {
    public interface IIntegrationEvent { }
}
namespace Test {
    [EricksonLopez.Outbox.Contracts.OutboxMessage]
    public class MyEvent : EricksonLopez.Events.IIntegrationEvent { }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Any(d => d.Id == "OUTBOX011").Should().BeFalse();
    }

    [Fact]
    public async Task EarlyExits_Should_Be_Covered()
    {
        var source = @"
using System;

// Global namespace class
public class GlobalMessage { }

namespace Test {
    public class OtherBuilder {
        public System.Threading.Tasks.Task StoreAsync() => default;
        public OtherBuilder Property => this;
        public void Publish() { }
    }
    public class Usage {
        public System.Action StoreAsync;

        public async System.Threading.Tasks.Task DoWork() {
            // Not a MemberAccessExpressionSyntax
            StoreAsync?.Invoke();

            // StoreAsync on another type
            var builder = new OtherBuilder();
            await builder.StoreAsync();

            // StoreAsync with plain member access in chain
            await builder.Property.StoreAsync();

            // Unresolved StoreAsync (using dynamic)
            dynamic d = builder;
            await d.StoreAsync();

            // Invocation expression with no member access
            System.Func<System.Threading.Tasks.Task> func = () => default;
            await func();

            // Publish on another type (covers OUTBOX001/002)
            builder.Publish();
        }

        // Generic Publish<T> inside generic method (ITypeParameterSymbol)
        public void PublishGeneric<T>(EricksonLopez.Outbox.IOutbox outbox, T msg) where T : notnull {
            outbox.Publish(msg);
        }
    }

    public class Setup {
        public void Configure() {
            // Unresolved method (tests returning early when symbol is null)
            UnknownMethod();

            // Method name is not AddOutbox (covers OUTBOX005)
            AddSomethingElse(opts => { });

            // AddOutbox with no arguments
            AddOutbox();

            // AddOutbox with argument but not a lambda
            AddOutbox(new Action<object>(opts => { }));
            
            // AddOutbox with non-invocation expression (we include UseSerializer so it doesn't fail OUTBOX005)
            AddOutbox(opts => { var a = 1; Opts.UseSerializer(); });
            
            // AddOutbox with plain IdentifierName
            AddOutbox(opts => { Opts.UseGeneratedTypes(); Opts.UseSerializer(); });
        }

        public void UnknownMethod() { }
        public void AddSomethingElse(Action<object> opts) { }
        public void AddOutbox() { }
        public void AddOutbox(Action<object> opts) { }
    }
    
    public class Opts {
        public static void UseGeneratedTypes() { }
        public static void UseSerializer() { }
    }

    // Class without IIntegrationEvent (covers OUTBOX011)
    public class NotAnEvent { }
    
    // Class inheriting IIntegrationEvent but from a different namespace
    public interface IIntegrationEvent { }
    public class FakeEvent : IIntegrationEvent { }

    // Struct implementing consumer (covers OUTBOX003)
    public struct StructConsumer : MassTransit.IConsumer<object> { }

    // Abstract class implementing consumer (covers OUTBOX003)
    public abstract class AbstractConsumer : MassTransit.IConsumer<object> { }
}

namespace MassTransit {
    public interface IConsumer<T> { }
}

// Struct implementing publisher (covers OUTBOX012 type kind early exit)
public interface IOtherPublisher : EricksonLopez.Outbox.IBrokerPublisher { }

namespace EricksonLopez.Outbox {
    public interface IOutbox {
        System.Threading.Tasks.ValueTask Publish<T>(T message) where T : notnull;
    }
    public interface IBrokerPublisher {
        System.Threading.Tasks.Task<object> PublishRawAsync();
    }
    public class OutboxRetryPolicy {
        public OutboxRetryPolicy(int maxAttempts) { }
    }
}
namespace TestRetry {
    public class RetryUsage {
        public void Configure() {
            // Non-constant maxAttempts
            int val = 5;
            var policy = new EricksonLopez.Outbox.OutboxRetryPolicy(val);
        }
    }
}
namespace TestAssignment {
    public class AssignmentUsage {
        public void Configure() {
            EricksonLopez.Outbox.OutboxRetryPolicy x;
            x = new EricksonLopez.Outbox.OutboxRetryPolicy(5);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().BeEmpty();
    }
}

public class TransactionRequiredAnalyzerTests
{
    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("TestProj", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var document = project.AddDocument("Test.cs", source);

        var compilation = await document.Project.GetCompilationAsync();
        var compilationWithAnalyzers = compilation!.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new TransactionRequiredAnalyzer()));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    [Fact]
    public async Task MissingTransaction_Should_Report_OUTBOX010()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class OutboxMessageBuilder<T> {
        public OutboxMessageBuilder<T> WithTransaction(object t) => this;
        public System.Threading.Tasks.ValueTask StoreAsync() => default;
    }
    public interface IOutbox {
        OutboxMessageBuilder<T> Publish<T>(T message) where T : notnull;
    }
}
namespace Test {
    public class MyMessage { }
    public class Usage {
        public async System.Threading.Tasks.Task DoWork(EricksonLopez.Outbox.IOutbox outbox) {
            // Missing WithTransaction
            await outbox.Publish(new MyMessage()).StoreAsync();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Any(d => d.Id == "OUTBOX010").Should().BeTrue();
    }

    [Fact]
    public async Task HasTransaction_Should_Not_Report_OUTBOX010()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class OutboxMessageBuilder<T> {
        public OutboxMessageBuilder<T> WithTransaction(object t) => this;
        public System.Threading.Tasks.ValueTask StoreAsync() => default;
    }
    public interface IOutbox {
        OutboxMessageBuilder<T> Publish<T>(T message) where T : notnull;
    }
}
namespace Test {
    public class MyMessage { }
    public class Usage {
        public async System.Threading.Tasks.Task DoWork(EricksonLopez.Outbox.IOutbox outbox, object tx) {
            // Correct: WithTransaction is present
            await outbox.Publish(new MyMessage()).WithTransaction(tx).StoreAsync();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Any(d => d.Id == "OUTBOX010").Should().BeFalse();
    }

    [Fact]
    public async Task EarlyExits_Should_Be_Covered()
    {
        var source = @"
namespace Test {
    public class OtherBuilder {
        public System.Threading.Tasks.Task StoreAsync() => default;
        public OtherBuilder Property => this;
    }
    public class Usage {
        public System.Action StoreAsync;

        public async System.Threading.Tasks.Task DoWork() {
            // Not a MemberAccessExpressionSyntax
            StoreAsync?.Invoke();

            // StoreAsync on another type
            var builder = new OtherBuilder();
            await builder.StoreAsync();

            // StoreAsync with plain member access in chain
            await builder.Property.StoreAsync();

            // Unresolved StoreAsync
            // await Unknown.StoreAsync(); // Compilation error but tests early exits if symbol not found

            // Invocation expression with no member access
            System.Func<System.Threading.Tasks.Task> func = () => default;
            await func();
        }
    }
}
namespace EricksonLopez.Outbox {
    public class OutboxMessageBuilder<T> {
        public System.Threading.Tasks.ValueTask StoreAsync() => default;
    }
}
namespace Test2 {
    public class Usage2 {
        public async System.Threading.Tasks.Task DoWork(EricksonLopez.Outbox.OutboxMessageBuilder<object> builder, System.Func<EricksonLopez.Outbox.OutboxMessageBuilder<object>> factory) {
            // IdentifierNameSyntax as expression
            await builder.StoreAsync();

            // Invocation without MemberAccess
            await factory().StoreAsync();

            // Unresolved symbol
            dynamic d = builder;
            await d.StoreAsync();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX010").Should().Be(2);
    }
}
