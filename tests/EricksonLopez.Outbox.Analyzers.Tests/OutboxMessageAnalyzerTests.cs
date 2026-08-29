// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
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
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToList();

        var project = workspace.AddProject("TestProj", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(references);

        var document = project.AddDocument("Test.cs", source);

        var compilation = await document.Project.GetCompilationAsync();
        Exception? analyzerException = null;
        var options = new CompilationWithAnalyzersOptions(
            new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            onAnalyzerException: (ex, _, _) => analyzerException = ex,
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: false,
            reportSuppressedDiagnostics: false);
        var compilationWithAnalyzers = compilation!.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new OutboxMessageAnalyzer(), new TransactionRequiredAnalyzer()), options);
        var diags = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        if (analyzerException != null)
        {
            throw new InvalidOperationException($"Analyzer threw exception: {analyzerException.Message}", analyzerException);
        }
        var allDiags = await compilationWithAnalyzers.GetAllDiagnosticsAsync();
        var adErrors = allDiags.Where(d => d.Id == "AD0001").ToList();
        if (adErrors.Count > 0)
        {
            throw new InvalidOperationException($"Analyzer exception AD0001: {adErrors[0].GetMessage()}");
        }
        return diags;
    }

    [Fact]
    public async Task MissingOutboxMessage_Should_Report_OUTBOX002()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        ValueTask Publish<T>(T message) where T : notnull;
    }
}
namespace Test {
    public class MyMessage { }
    public class Usage {
        public async Task DoWork(EricksonLopez.Outbox.IOutbox outbox) {
            await outbox.Publish(new MyMessage());
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().Contain(d => d.Id == "OUTBOX002");
    }

    [Fact]
    public async Task MissingIdProperty_Should_Report_OUTBOX001()
    {
        var source = @"
namespace EricksonLopez.Outbox.Contracts { public class OutboxMessageAttribute : System.Attribute {} }
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        ValueTask Publish<T>(T message) where T : notnull;
    }
}
namespace Test {
    [EricksonLopez.Outbox.Contracts.OutboxMessage]
    public class MyMessage { }
    public class Usage {
        public async Task DoWork(EricksonLopez.Outbox.IOutbox outbox) {
            await outbox.Publish(new MyMessage());
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().Contain(d => d.Id == "OUTBOX001");
    }

    [Fact]
    public async Task HasIdProperty_Should_Not_Report_OUTBOX001()
    {
        var source = @"
namespace EricksonLopez.Outbox.Contracts { public class OutboxMessageAttribute : System.Attribute {} }
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        ValueTask Publish<T>(T message) where T : notnull;
    }
}
namespace Test {
    [EricksonLopez.Outbox.Contracts.OutboxMessage]
    public class MyMessage {
        public Guid Id { get; set; }
    }
    public class Usage {
        public async Task DoWork(EricksonLopez.Outbox.IOutbox outbox) {
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
        diags.Should().Contain(d => d.Id == "OUTBOX003");
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
        diags.Should().Contain(d => d.Id == "OUTBOX005");
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
        diags.Should().NotContain(d => d.Id == "OUTBOX005");
    }

    [Fact]
    public async Task AbandonedBuilder_Should_Report_OUTBOX008()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class OutboxMessageBuilder<T> {
        public OutboxMessageBuilder<T> WithTransaction(object t) => this;
        public ValueTask StoreAsync() => default;
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
        diags.Should().Contain(d => d.Id == "OUTBOX008");
    }

    [Fact]
    public async Task CapturedBuilder_Should_Not_Report_OUTBOX008()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class OutboxMessageBuilder<T> {
        public OutboxMessageBuilder<T> WithTransaction(object t) => this;
        public ValueTask StoreAsync() => default;
    }
    public interface IOutbox {
        OutboxMessageBuilder<T> Publish<T>(T message) where T : notnull;
    }
}
namespace Test {
    public class MyMessage { }
    public class Usage {
        public async Task DoWork(EricksonLopez.Outbox.IOutbox outbox) {
            var builder = outbox.Publish(new MyMessage());
            await builder.StoreAsync();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().NotContain(d => d.Id == "OUTBOX008");
    }

    [Fact]
    public async Task DefaultDispatchResult_Should_Report_OUTBOX012()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IBrokerPublisher {
        Task<object> PublishRawAsync();
    }
}
namespace Test {
    public class BadPublisher : EricksonLopez.Outbox.IBrokerPublisher {
        public Task<object> PublishRawAsync() {
            return default;
        }
    }
    public class BadPublisher2 : EricksonLopez.Outbox.IBrokerPublisher {
        public Task<object> PublishRawAsync() {
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
        Task<object> PublishRawAsync();
    }
}
namespace Test {
    public class GoodPublisher : EricksonLopez.Outbox.IBrokerPublisher {
        public Task<object> PublishRawAsync() {
            return Task.FromResult(new object());
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().NotContain(d => d.Id == "OUTBOX012");
    }

    [Fact]
    public async Task MissingJsonSerializable_Should_Report_OUTBOX013()
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
        diags.Should().Contain(d => d.Id == "OUTBOX013");
    }

    [Fact]
    public async Task HasJsonSerializable_Should_Not_Report_OUTBOX013()
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
        diags.Should().NotContain(d => d.Id == "OUTBOX013");
    }

    [Fact]
    public async Task MissingOutboxMessageOnIntegrationEvent_Should_Report_OUTBOX006()
    {
        var source = @"
namespace EricksonLopez.Outbox.Abstractions {
    public interface IIntegrationEvent { }
}
namespace Test {
    public class OrderCreatedEvent : EricksonLopez.Outbox.Abstractions.IIntegrationEvent { }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().Contain(d => d.Id == "OUTBOX006");
    }

    [Fact]
    public async Task HasOutboxMessageOnIntegrationEvent_Should_Not_Report_OUTBOX006()
    {
        var source = @"
namespace EricksonLopez.Outbox.Abstractions {
    public interface IIntegrationEvent { }
}
namespace EricksonLopez.Outbox.Contracts {
    public class OutboxMessageAttribute : System.Attribute {
        public OutboxMessageAttribute(string alias) { }
    }
}
namespace Test {
    [EricksonLopez.Outbox.Contracts.OutboxMessage(""order.created"")]
    public class OrderCreatedEvent : EricksonLopez.Outbox.Abstractions.IIntegrationEvent { }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().NotContain(d => d.Id == "OUTBOX006");
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
        diags.Should().Contain(d => d.Id == "OUTBOX007");
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
        diags.Should().Contain(d => d.Id == "OUTBOX009");
    }

    [Fact]
    public async Task MissingIntegrationEventAlias_Should_Report_OUTBOX006()
    {
        var source = @"
namespace EricksonLopez.Events {
    public interface IIntegrationEvent { }
}
namespace Test {
    public class MyEvent : EricksonLopez.Events.IIntegrationEvent { }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().Contain(d => d.Id == "OUTBOX006");
    }

    [Fact]
    public async Task HasIntegrationEventAlias_Should_Not_Report_OUTBOX006()
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
        diags.Should().NotContain(d => d.Id == "OUTBOX006");
    }

    [Fact]
    public async Task EarlyExits_Should_Be_Covered()
    {
        var source = @"

// Global namespace class
public class GlobalMessage { }

namespace Test {
    public class OtherBuilder {
        public Task StoreAsync() => default;
        public OtherBuilder Property => this;
        public void Publish() { }
    }
    public class Usage {
        public System.Action StoreAsync;

        public async Task DoWork() {
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
            System.Func<Task> func = () => default;
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
        ValueTask Publish<T>(T message) where T : notnull;
    }
    public interface IBrokerPublisher {
        Task<object> PublishRawAsync();
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
        Exception? analyzerException = null;
        var options = new CompilationWithAnalyzersOptions(
            new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            onAnalyzerException: (ex, _, _) => analyzerException = ex,
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: false,
            reportSuppressedDiagnostics: false);
        var compilationWithAnalyzers = compilation!.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new TransactionRequiredAnalyzer()), options);
        var diags = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        if (analyzerException != null)
        {
            throw new InvalidOperationException($"Analyzer threw exception: {analyzerException.Message}", analyzerException);
        }
        var allDiags = await compilationWithAnalyzers.GetAllDiagnosticsAsync();
        var adErrors = allDiags.Where(d => d.Id == "AD0001").ToList();
        if (adErrors.Count > 0)
        {
            throw new InvalidOperationException($"Analyzer exception AD0001: {adErrors[0].GetMessage()}");
        }
        return diags;
    }

    [Fact]
    public async Task MissingTransaction_Should_Report_OUTBOX010()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class OutboxMessageBuilder<T> {
        public OutboxMessageBuilder<T> WithTransaction(object t) => this;
        public ValueTask StoreAsync() => default;
    }
    public interface IOutbox {
        OutboxMessageBuilder<T> Publish<T>(T message) where T : notnull;
    }
}
namespace Test {
    public class MyMessage { }
    public class Usage {
        public async Task DoWork(EricksonLopez.Outbox.IOutbox outbox) {
            // Missing WithTransaction
            await outbox.Publish(new MyMessage()).StoreAsync();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().Contain(d => d.Id == "OUTBOX010");
    }

    [Fact]
    public async Task HasTransaction_Should_Not_Report_OUTBOX010()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class OutboxMessageBuilder<T> {
        public OutboxMessageBuilder<T> WithTransaction(object t) => this;
        public ValueTask StoreAsync() => default;
    }
    public interface IOutbox {
        OutboxMessageBuilder<T> Publish<T>(T message) where T : notnull;
    }
}
namespace Test {
    public class MyMessage { }
    public class Usage {
        public async Task DoWork(EricksonLopez.Outbox.IOutbox outbox, object tx) {
            // Correct: WithTransaction is present
            await outbox.Publish(new MyMessage()).WithTransaction(tx).StoreAsync();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().NotContain(d => d.Id == "OUTBOX010");
    }

    [Fact]
    public async Task EarlyExits_Should_Be_Covered()
    {
        var source = @"
namespace Test {
    public class OtherBuilder {
        public Task StoreAsync() => default;
        public OtherBuilder Property => this;
    }
    public class Usage {
        public System.Action StoreAsync;

        public async Task DoWork() {
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
            System.Func<Task> func = () => default;
            await func();
        }
    }
}
namespace EricksonLopez.Outbox {
    public class OutboxMessageBuilder<T> {
        public ValueTask StoreAsync() => default;
    }
}
namespace Test2 {
    public class Usage2 {
        public async Task DoWork(EricksonLopez.Outbox.OutboxMessageBuilder<object> builder, System.Func<EricksonLopez.Outbox.OutboxMessageBuilder<object>> factory) {
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

    [Fact]
    public async Task TransactionRequired_With_PlainMemberAccess_Should_Report_OUTBOX010()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class OutboxMessageBuilder<T> {
        public ValueTask StoreAsync() => default;
    }
}
namespace Test {
    public class Holder {
        public EricksonLopez.Outbox.OutboxMessageBuilder<object> Builder { get; set; }
    }
    public class Usage {
        public async Task DoWork(Holder holder) {
            await holder.Builder.StoreAsync();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().Contain(d => d.Id == "OUTBOX010");
    }
}

public class OutboxMessageAnalyzerEdgeCasesTests
{
    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("TestProj", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var document = project.AddDocument("Test.cs", source);

        var compilation = await document.Project.GetCompilationAsync();
        Exception? analyzerException = null;
        var options = new CompilationWithAnalyzersOptions(
            new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            onAnalyzerException: (ex, _, _) => analyzerException = ex,
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: false,
            reportSuppressedDiagnostics: false);
        var compilationWithAnalyzers = compilation!.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new OutboxMessageAnalyzer(), new TransactionRequiredAnalyzer()), options);
        var diags = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        if (analyzerException != null)
        {
            throw new InvalidOperationException($"Analyzer threw exception: {analyzerException.Message}", analyzerException);
        }
        var allDiags = await compilationWithAnalyzers.GetAllDiagnosticsAsync();
        var adErrors = allDiags.Where(d => d.Id == "AD0001").ToList();
        if (adErrors.Count > 0)
        {
            throw new InvalidOperationException($"Analyzer exception AD0001: {adErrors[0].GetMessage()}");
        }
        return diags;
    }

    [Fact]
    public async Task OtherMethods_On_IOutbox_Should_Be_Ignored()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        void OtherMethod<T>(T msg);
    }
}
namespace Test {
    public class Msg { }
    public class Usage {
        public void Do(EricksonLopez.Outbox.IOutbox outbox) {
            outbox.OtherMethod(new Msg());
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().BeEmpty();
    }

    [Fact]
    public async Task StoreAsync_With_NonLiteral_Or_NonZero_Literal_Should_Not_Report_OUTBOX007()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        void StoreAsync<T>(T msg, object tx);
    }
}
namespace Test {
    public class Msg {
        public Guid Id { get; set; }
    }
    public class Usage {
        public void Do(EricksonLopez.Outbox.IOutbox outbox, object myTx) {
            outbox.StoreAsync(new Msg(), myTx);
            outbox.StoreAsync(new Msg(), 123);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX007").Should().BeEmpty();
    }

    [Fact]
    public async Task AddOutbox_With_LocalFunction_Serializer_Should_Be_Recognized()
    {
        var source = @"
namespace Microsoft.Extensions.DependencyInjection {
    public interface IServiceCollection { }
}
namespace EricksonLopez.Outbox {
    public class OutboxOptions {
        public void UseSerializer() {}
    }
    public static class OutboxServiceCollectionExtensions {
        public static void AddOutbox(this Microsoft.Extensions.DependencyInjection.IServiceCollection services, System.Action<OutboxOptions> configure) {}
    }
}
namespace Test {
    public class Startup {
        public void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services) {
            void UseSerializer() {}
            EricksonLopez.Outbox.OutboxServiceCollectionExtensions.AddOutbox(services, opt => {
                UseSerializer();
            });
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX007").Should().BeEmpty();
    }

    [Fact]
    public void SupportedDiagnostics_Should_Contain_All_Descriptors()
    {
        var analyzer = new OutboxMessageAnalyzer();
        analyzer.SupportedDiagnostics.Should().HaveCount(11);
        var expectedIds = new[]
        {
            "OUTBOX001", "OUTBOX002", "OUTBOX003", "OUTBOX004", "OUTBOX005",
            "OUTBOX006", "OUTBOX007", "OUTBOX008", "OUTBOX009", "OUTBOX012", "OUTBOX013"
        };
        foreach (var id in expectedIds)
        {
            var descriptor = analyzer.SupportedDiagnostics.FirstOrDefault(d => d.Id == id);
            descriptor.Should().NotBeNull();
            descriptor!.IsEnabledByDefault.Should().BeTrue();
            descriptor.DefaultSeverity.Should().BeOneOf(DiagnosticSeverity.Error, DiagnosticSeverity.Warning);
        }
    }

    [Fact]
    public async Task Consumer_With_Attributes_Should_Not_Report_OUTBOX003()
    {
        var source = @"
namespace EricksonLopez.Outbox.Contracts {
    public class InboxConsumerAttribute : System.Attribute {}
    public class IdempotentConsumerAttribute : System.Attribute {}
}
namespace Test {
    public interface IConsumer<T> {}
    public interface IHandleMessages<T> {}
    public interface IMessageHandler<T> {}

    [EricksonLopez.Outbox.Contracts.InboxConsumer]
    public class ConsumerA : IConsumer<object> {}

    [EricksonLopez.Outbox.Contracts.IdempotentConsumer]
    public class ConsumerB : IHandleMessages<object> {}

    public abstract class AbstractConsumer : IMessageHandler<object> {}
    public struct StructConsumer : IConsumer<object> {}
    public interface ICustomConsumer : IConsumer<object> {}
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX003").Should().BeEmpty();
    }

    [Fact]
    public async Task IntegrationEvent_Variants_Should_Be_Analyzed_Correctly()
    {
        var source = @"
namespace EricksonLopez.Events.Contracts {
    public interface IIntegrationEvent {}
}
namespace EricksonLopez.Outbox.Contracts {
    public class OutboxMessageAttribute : System.Attribute {}
}
namespace OtherNamespace {
    public interface IIntegrationEvent {}
}
namespace Test {
    // Should report OUTBOX006
    public struct StructEvent : EricksonLopez.Events.Contracts.IIntegrationEvent {}

    // Should NOT report OUTBOX006 (abstract)
    public abstract class AbstractEvent : EricksonLopez.Events.Contracts.IIntegrationEvent {}

    // Should NOT report OUTBOX006 (interface)
    public interface ISubEvent : EricksonLopez.Events.Contracts.IIntegrationEvent {}

    // Should NOT report OUTBOX006 (other namespace)
    public class OtherEvent : OtherNamespace.IIntegrationEvent {}

    // Should NOT report OUTBOX006 (has attribute)
    [EricksonLopez.Outbox.Contracts.OutboxMessage]
    public class ValidEvent : EricksonLopez.Events.Contracts.IIntegrationEvent {}
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX006").Should().Be(1);
    }

    [Fact]
    public async Task JsonSerializerContext_With_Matching_Attribute_Should_Not_Report_OUTBOX013()
    {
        var source = @"
namespace System.Text.Json.Serialization {
    public abstract class JsonSerializerContext {}
    public class JsonSerializableAttribute : System.Attribute {
        public JsonSerializableAttribute(System.Type type) {}
        public JsonSerializableAttribute() {}
    }
}
namespace EricksonLopez.Outbox.Contracts {
    public class OutboxMessageAttribute : System.Attribute {}
}
namespace Other {
    public abstract class JsonSerializerContext {}
}
namespace Test {
    [EricksonLopez.Outbox.Contracts.OutboxMessage]
    public class RegisteredMsg { public Guid Id { get; set; } }

    [System.Text.Json.Serialization.JsonSerializable(typeof(RegisteredMsg))]
    public class ValidContext : System.Text.Json.Serialization.JsonSerializerContext {}
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX013").Should().BeEmpty();
    }

    [Fact]
    public async Task BrokerPublisher_Returning_Default_Should_Report_OUTBOX012()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public struct DispatchResult {}
    public interface IBrokerPublisher {
        ValueTask<DispatchResult> PublishRawAsync(string topic, byte[] payload);
    }
}
namespace Test {
    public class BadPublisher1 : EricksonLopez.Outbox.IBrokerPublisher {
        public ValueTask<EricksonLopez.Outbox.DispatchResult> PublishRawAsync(string topic, byte[] payload) {
            return default;
        }
    }
    public class BadPublisher2 : EricksonLopez.Outbox.IBrokerPublisher {
        public ValueTask<EricksonLopez.Outbox.DispatchResult> PublishRawAsync(string topic, byte[] payload) {
            return default(ValueTask<EricksonLopez.Outbox.DispatchResult>);
        }
    }
    public class NonPublisher {
        public int PublishRawAsync() => default;
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX012").Should().Be(2);
    }

    [Fact]
    public async Task RetryPolicy_With_Negative_Or_Over100_Should_Report_OUTBOX004()
    {
        var source = @"
namespace Test {
    public class FixedDelayRetryPolicy {
        public FixedDelayRetryPolicy(int maxAttempts) {}
    }
    public class OtherClass {
        public OtherClass(int maxAttempts) {}
    }
    public class Usage {
        public void Run() {
            var p1 = new FixedDelayRetryPolicy(maxAttempts: -5);
            var p2 = new FixedDelayRetryPolicy(maxAttempts: 105);
            var p3 = new FixedDelayRetryPolicy(maxAttempts: 5);
            var o = new OtherClass(maxAttempts: -5);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX004").Should().Be(2);
    }

    [Fact]
    public async Task StoreAsync_With_Named_Null_Transaction_Should_Report_OUTBOX007()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        Task StoreAsync<T>(T message, object transaction = null) where T : notnull;
    }
}
namespace Test {
    public class MyMessage { public Guid Id { get; set; } }
    public class NonOutbox {
        public void StoreAsync(object msg, object transaction) {}
    }
    public class Usage {
        public async Task DoWork(EricksonLopez.Outbox.IOutbox outbox, NonOutbox other) {
            await outbox.StoreAsync(new MyMessage(), transaction: null);
            other.StoreAsync(new MyMessage(), null);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX007").Should().Be(1);
    }

    [Fact]
    public async Task AbandonedBuilder_Custom_Interface_Should_Report_OUTBOX008()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutboxMessageBuilder {
        Task StoreAsync(object tx);
    }
    public class CustomBuilder : IOutboxMessageBuilder {
        public Task StoreAsync(object tx) => Task.CompletedTask;
    }
    public interface IOutbox {
        CustomBuilder Publish<T>(T message);
    }
}
namespace Test {
    public class Usage {
        public void DoWork(EricksonLopez.Outbox.IOutbox outbox) {
            outbox.Publish(123);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX008").Should().Be(1);
    }

    [Fact]
    public async Task Assignment_To_Non_OutboxOptions_MaxRetryCount_Should_Not_Report_OUTBOX009()
    {
        var source = @"
namespace Test {
    public class OtherConfig {
        public int MaxRetryCount { get; set; }
    }
    public class Usage {
        public void Configure() {
            var cfg = new OtherConfig();
            cfg.MaxRetryCount = 0;
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX009").Should().BeEmpty();
    }

    [Fact]
    public async Task Type_With_NonGuid_Id_Should_Report_OUTBOX001()
    {
        var source = @"
namespace EricksonLopez.Outbox.Contracts { public class OutboxMessageAttribute : System.Attribute {} }
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        ValueTask Publish<T>(T message) where T : notnull;
    }
}
namespace Test {
    [EricksonLopez.Outbox.Contracts.OutboxMessage]
    public class NonGuidIdMsg {
        public int Id { get; set; }
    }
    public class Usage {
        public async Task Do(EricksonLopez.Outbox.IOutbox outbox) {
            await outbox.Publish(new NonGuidIdMsg());
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX001").Should().Be(1);
    }

    [Fact]
    public async Task Custom_Type_Implementing_IOutbox_Interface_Should_Trigger_Analyzers()
    {
        var source = @"
namespace EricksonLopez.Outbox.Contracts { public class OutboxMessageAttribute : System.Attribute {} }
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        Task StoreAsync<T>(T message, object tx = null) where T : notnull;
    }
}
namespace Test {
    public class CustomOutbox : EricksonLopez.Outbox.IOutbox {
        public Task StoreAsync<T>(T message, object tx = null) where T : notnull => Task.CompletedTask;
    }
    public class BadMsg {}
    public class Usage {
        public async Task Do(CustomOutbox outbox) {
            await outbox.StoreAsync(new BadMsg(), null);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().Contain(d => d.Id == "OUTBOX002");
        diags.Should().Contain(d => d.Id == "OUTBOX007");
    }

    [Fact]
    public async Task RetryPolicy_Boundaries_1_And_100_Should_Not_Report_OUTBOX004()
    {
        var source = @"
namespace Test {
    public class FixedDelayRetryPolicy {
        public FixedDelayRetryPolicy(int maxAttempts) {}
    }
    public class Usage {
        public void Run() {
            var p1 = new FixedDelayRetryPolicy(1);
            var p2 = new FixedDelayRetryPolicy(100);
            var p3 = new FixedDelayRetryPolicy(maxAttempts: 100);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX004").Should().BeEmpty();
    }

    [Fact]
    public async Task AddOutbox_With_Parenthesized_Serializer_Calls_Should_Not_Report_OUTBOX005()
    {
        var source = @"
namespace Microsoft.Extensions.DependencyInjection { public interface IServiceCollection {} }
namespace EricksonLopez.Outbox {
    public class OutboxOptions {
        public void UseGeneratedTypes() {}
        public void UseGeneratedTypesAndSerialization() {}
    }
    public static class OutboxServiceCollectionExtensions {
        public static void AddOutbox(this Microsoft.Extensions.DependencyInjection.IServiceCollection services, System.Action<OutboxOptions> configure) {}
    }
}
namespace Test {
    public class Startup {
        public void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services) {
            services.AddOutbox((opt) => opt.UseGeneratedTypes());
            services.AddOutbox((opt) => { opt.UseGeneratedTypesAndSerialization(); });
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX005").Should().BeEmpty();
    }

    [Fact]
    public async Task StoreAsync_With_Non_Transaction_Null_Argument_Should_Not_Report_OUTBOX007()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        Task StoreAsync<T>(T message, object customParam, object transaction) where T : notnull;
    }
}
namespace Test {
    public class MyMsg { public Guid Id { get; set; } }
    public class Usage {
        public async Task Do(EricksonLopez.Outbox.IOutbox outbox, object activeTx) {
            await outbox.StoreAsync(new MyMsg(), customParam: null, transaction: activeTx);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX007").Should().BeEmpty();
    }

    [Fact]
    public async Task BrokerPublisher_Struct_And_Multiple_Returns_Should_Report_OUTBOX012()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public struct DispatchResult {
        public static DispatchResult Ok() => default;
    }
    public interface IBrokerPublisher {
        ValueTask<DispatchResult> PublishRawAsync(string topic, byte[] payload);
    }
}
namespace OtherNamespace {
    public interface IBrokerPublisher {
        ValueTask<object> PublishRawAsync(string topic, byte[] payload);
    }
}
namespace Test {
    public struct StructPublisher : EricksonLopez.Outbox.IBrokerPublisher {
        public ValueTask<EricksonLopez.Outbox.DispatchResult> PublishRawAsync(string topic, byte[] payload) {
            return default;
        }
    }
    public class MultipleReturnsPublisher : EricksonLopez.Outbox.IBrokerPublisher {
        public ValueTask<EricksonLopez.Outbox.DispatchResult> PublishRawAsync(string topic, byte[] payload) {
            if (topic.Length > 0)
                return ValueTask.FromResult(EricksonLopez.Outbox.DispatchResult.Ok());
            return default;
        }
    }
    public class OtherNamespacePublisher : OtherNamespace.IBrokerPublisher {
        public ValueTask<object> PublishRawAsync(string topic, byte[] payload) {
            return default;
        }
    }
    public abstract class AbstractPublisher : EricksonLopez.Outbox.IBrokerPublisher {
        public abstract ValueTask<EricksonLopez.Outbox.DispatchResult> PublishRawAsync(string topic, byte[] payload);
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX012").Should().Be(2);
    }

    [Fact]
    public async Task OtherVendor_OutboxMessageAttribute_Should_Report_OUTBOX002()
    {
        var source = @"
namespace OtherVendor {
    public class OutboxMessageAttribute : System.Attribute {}
}
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        ValueTask Publish<T>(T message) where T : notnull;
    }
}
namespace Test {
    [OtherVendor.OutboxMessage]
    public class OtherVendorMsg { public Guid Id { get; set; } }
    public class Usage {
        public async Task Do(EricksonLopez.Outbox.IOutbox outbox) {
            await outbox.Publish(new OtherVendorMsg());
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX002").Should().Be(1);
    }

    [Fact]
    public async Task NonOutbox_Type_In_EricksonLopez_Outbox_Namespace_Should_Not_Trigger_OUTBOX002()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class OtherService {
        public void Publish<T>(T message) {}
    }
}
namespace Test {
    public class PlainMsg { public Guid Id { get; set; } }
    public class Usage {
        public void Do(EricksonLopez.Outbox.OtherService service) {
            service.Publish(new PlainMsg());
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX002").Should().BeEmpty();
    }

    [Fact]
    public async Task RetryPolicy_Named_MaxAttempts_Zero_And_Positional_With_Multiple_Args_Should_Report_OUTBOX004()
    {
        var source = @"
namespace Test {
    public class CustomRetryPolicy {
        public CustomRetryPolicy(string name, int maxAttempts) {}
    }
    public class OtherRetryPolicy {
        public OtherRetryPolicy(int a, int b, int c) {}
    }
    public class Usage {
        public void Run() {
            var p1 = new CustomRetryPolicy(""policy"", 0);
            var p2 = new CustomRetryPolicy(name: ""policy"", maxAttempts: 0);
            var p3 = new OtherRetryPolicy(1, 2, 3);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX004").Should().Be(2);
    }

    [Fact]
    public async Task AbandonedBuilder_In_Other_Namespace_Should_Not_Report_OUTBOX008()
    {
        var source = @"
namespace OtherVendor {
    public class OutboxMessageBuilder {
        public void DoWork() {}
    }
    public interface IOutboxMessageBuilder {}
    public class CustomOtherBuilder : IOutboxMessageBuilder {}
}
namespace Test {
    public class Usage {
        public OtherVendor.OutboxMessageBuilder GetBuilder() => new();
        public OtherVendor.CustomOtherBuilder GetCustom() => new();
        public void Run() {
            GetBuilder();
            GetCustom();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX008").Should().BeEmpty();
    }

    private sealed class MockAnalysisContext : AnalysisContext
    {
        public GeneratedCodeAnalysisFlags? GeneratedCodeFlags { get; private set; }
        public bool IsConcurrentExecutionConfigured { get; private set; }

        public override void ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags analysisMode)
        {
            GeneratedCodeFlags = analysisMode;
        }

        public override void EnableConcurrentExecution()
        {
            IsConcurrentExecutionConfigured = true;
        }

        public override void RegisterCompilationAction(System.Action<CompilationAnalysisContext> action) { }
        public override void RegisterCompilationStartAction(System.Action<CompilationStartAnalysisContext> action) { }
        public override void RegisterCodeBlockAction(System.Action<CodeBlockAnalysisContext> action) { }
        public override void RegisterCodeBlockStartAction<TLanguageKindEnum>(System.Action<CodeBlockStartAnalysisContext<TLanguageKindEnum>> action) where TLanguageKindEnum : struct { }
        public override void RegisterSemanticModelAction(System.Action<SemanticModelAnalysisContext> action) { }
        public override void RegisterSymbolAction(System.Action<SymbolAnalysisContext> action, ImmutableArray<SymbolKind> symbolKinds) { }
        public override void RegisterSyntaxNodeAction<TLanguageKindEnum>(System.Action<SyntaxNodeAnalysisContext> action, ImmutableArray<TLanguageKindEnum> syntaxKinds) where TLanguageKindEnum : struct { }
        public override void RegisterSyntaxTreeAction(System.Action<SyntaxTreeAnalysisContext> action) { }
        public override void RegisterOperationAction(System.Action<OperationAnalysisContext> action, ImmutableArray<OperationKind> operationKinds) { }
        public override void RegisterOperationBlockAction(System.Action<OperationBlockAnalysisContext> action) { }
        public override void RegisterOperationBlockStartAction(System.Action<OperationBlockStartAnalysisContext> action) { }
        public override void RegisterAdditionalFileAction(System.Action<AdditionalFileAnalysisContext> action) { }
    }

    [Fact]
    public void Initialize_Should_Configure_Generated_Code_And_Concurrent_Execution()
    {
        var context = new MockAnalysisContext();
        var analyzer = new OutboxMessageAnalyzer();
        analyzer.Initialize(context);

        context.GeneratedCodeFlags.Should().Be(GeneratedCodeAnalysisFlags.None);
        context.IsConcurrentExecutionConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task RetryPolicy_Named_Exact_TypeName_Should_Report_OUTBOX004()
    {
        var source = @"
namespace Test {
    public class RetryPolicy {
        public RetryPolicy(int maxAttempts) {}
    }
    public class Usage {
        public void Run() {
            var p = new RetryPolicy(0);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX004").Should().Be(1);
    }

    [Fact]
    public async Task NonEricksonLopez_IOutbox_Interface_Should_Not_Report_OUTBOX002()
    {
        var source = @"
namespace OtherVendor {
    public interface IOutbox {
        void StoreAsync<T>(T msg, object tx);
    }
}
namespace Test {
    public class OtherOutboxImpl : OtherVendor.IOutbox {
        public void StoreAsync<T>(T msg, object tx) {}
    }
    public class UnregisteredMsg {}
    public class Usage {
        public void Run(OtherOutboxImpl impl) {
            impl.StoreAsync(new UnregisteredMsg(), null);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX002" || d.Id == "OUTBOX007").Should().BeEmpty();
    }

    [Fact]
    public async Task NonEricksonLopez_OtherService_StoreAsync_Should_Not_Report_OUTBOX007()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class OutboxWorkerService {
        public void StoreAsync(object msg, object tx) {}
    }
}
namespace Test {
    public class Usage {
        public void Run(EricksonLopez.Outbox.OutboxWorkerService s) {
            s.StoreAsync(123, null);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX007").Should().BeEmpty();
    }

    [Fact]
    public async Task JsonSerializerContext_With_Empty_Attributes_And_Unregistered_Msg_Should_Report_OUTBOX013()
    {
        var source = @"
namespace System.Text.Json.Serialization {
    public abstract class JsonSerializerContext {}
    public class JsonSerializableAttribute : System.Attribute {}
}
namespace EricksonLopez.Outbox.Contracts {
    public class OutboxMessageAttribute : System.Attribute {}
}
namespace Test {
    [EricksonLopez.Outbox.Contracts.OutboxMessage]
    public class MissingContextMsg { public Guid Id { get; set; } }

    [System.Text.Json.Serialization.JsonSerializable]
    public class EmptyAttrContext : System.Text.Json.Serialization.JsonSerializerContext {}
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX013").Should().Be(1);
    }

    [Fact]
    public async Task NonSystem_Context_Inheritance_Should_Not_Trigger_OUTBOX013()
    {
        var source = @"
namespace System.Text.Json.Serialization {
    public abstract class OtherContextBase {}
}
namespace EricksonLopez.Outbox.Contracts {
    public class OutboxMessageAttribute : System.Attribute {}
}
namespace Test {
    [EricksonLopez.Outbox.Contracts.OutboxMessage]
    public class UnregMsg { public Guid Id { get; set; } }

    public class CustomOtherContext : System.Text.Json.Serialization.OtherContextBase {}
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX013").Should().BeEmpty();
    }

    [Fact]
    public async Task BrokerPublisher_Without_PublishRawAsync_Method_Should_Not_Crash()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public struct DispatchResult {}
    public interface IBrokerPublisher {}
}
namespace Test {
    public class CustomEmptyPublisher : EricksonLopez.Outbox.IBrokerPublisher {}
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX012").Should().BeEmpty();
    }

    [Fact]
    public async Task RetryPolicy_With_More_Arguments_Than_Parameters_Should_Not_Crash()
    {
        var source = @"
namespace Test {
    public class CustomRetryPolicy {
        public CustomRetryPolicy(int maxAttempts, int delayMs) {}
    }
    public class Usage {
        public void Run() {
            var p = new CustomRetryPolicy(3, 100, 999);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX004").Should().BeEmpty();
    }

    [Fact]
    public async Task StoreAsync_With_More_Arguments_Than_Parameters_Should_Not_Crash()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        void StoreAsync<T>(T msg, object transaction);
    }
}
namespace Test {
    public class Usage {
        public void Run(EricksonLopez.Outbox.IOutbox outbox) {
            outbox.StoreAsync(123, new object(), 999);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX007").Should().BeEmpty();
    }

    [Fact]
    public async Task BrokerPublisher_With_Multiple_Interfaces_And_Default_Return_Should_Report_OUTBOX012()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public struct DispatchResult {}
    public interface IBrokerPublisher {
        ValueTask<DispatchResult> PublishRawAsync(string topic, byte[] payload);
    }
}
namespace Test {
    public class MultiInterfacePublisher : EricksonLopez.Outbox.IBrokerPublisher, IDisposable {
        public void Dispose() {}
        public ValueTask<EricksonLopez.Outbox.DispatchResult> PublishRawAsync(string topic, byte[] payload) {
            return default;
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX012").Should().Be(1);
    }

    [Fact]
    public async Task RetryPolicy_With_Object_Initializer_No_Parens_Should_Not_Crash()
    {
        var source = @"
namespace Test {
    public class CustomRetryPolicy {
        public int Delay { get; set; }
    }
    public class Usage {
        public void Run() {
            var p = new CustomRetryPolicy { Delay = 5 };
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX004").Should().BeEmpty();
    }

    [Fact]
    public async Task StoreAsync_With_Null_First_Arg_And_Valid_Transaction_Should_Not_Report_OUTBOX007()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        void StoreAsync<T>(T msg, object transaction);
    }
}
namespace Test {
    public class Usage {
        public void Run(EricksonLopez.Outbox.IOutbox outbox) {
            outbox.StoreAsync((string)null, new object());
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX007").Should().BeEmpty();
    }

    [Fact]
    public async Task StoreAsync_With_Named_Null_Transaction_And_Extra_Param_Should_Report_OUTBOX007()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        void StoreAsync<T>(T msg, object transaction, int extra = 0);
    }
}
namespace Test {
    public class Usage {
        public void Run(EricksonLopez.Outbox.IOutbox outbox) {
            outbox.StoreAsync(123, extra: 1, transaction: null);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX007").Should().Be(1);
    }

    [Fact]
    public async Task AddOutbox_With_No_Arguments_Should_Not_Crash()
    {
        var source = @"
namespace Test {
    public class ServiceCollection {
        public void AddOutbox() {}
    }
    public class Usage {
        public void Run(ServiceCollection services) {
            services.AddOutbox();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX005").Should().BeEmpty();
    }

    [Fact]
    public async Task StoreAsync_With_Single_Argument_Should_Not_Crash()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        void StoreAsync<T>(T msg);
    }
}
namespace Test {
    public class Usage {
        public void Run(EricksonLopez.Outbox.IOutbox outbox) {
            outbox.StoreAsync(123);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX007").Should().BeEmpty();
    }

    [Fact]
    public async Task OutboxOptions_With_NonZero_MaxRetryCount_Should_Not_Report_OUTBOX009()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class OutboxOptions {
        public int MaxRetryCount { get; set; }
    }
}
namespace Test {
    public class Usage {
        public void Run() {
            var opts = new EricksonLopez.Outbox.OutboxOptions();
            opts.MaxRetryCount = 5;
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX009").Should().BeEmpty();
    }

    [Fact]
    public async Task BrokerPublisher_With_NonDefault_Return_Should_Not_Report_OUTBOX012()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public struct DispatchResult {
        public static DispatchResult Success() => new();
    }
    public interface IBrokerPublisher {
        ValueTask<DispatchResult> PublishRawAsync(string topic, byte[] payload);
    }
}
namespace Test {
    public class ValidPublisher : EricksonLopez.Outbox.IBrokerPublisher {
        public ValueTask<EricksonLopez.Outbox.DispatchResult> PublishRawAsync(string topic, byte[] payload) {
            return ValueTask.FromResult(EricksonLopez.Outbox.DispatchResult.Success());
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX012").Should().BeEmpty();
    }

    [Fact]
    public async Task InfiniteRetries_ObjectCreationWithoutArgumentList_Should_Not_Throw_Or_Report()
    {
        var source = @"
namespace Test {
    public class RetryPolicy {
        public int MaxAttempts { get; set; }
    }
    public class Usage {
        public void Run() {
            var p = new RetryPolicy { MaxAttempts = 0 };
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().BeEmpty();
    }

    [Fact]
    public async Task InfiniteRetries_NonMaxAttempts_Zero_Should_Not_Report()
    {
        var source = @"
namespace Test {
    public class RetryPolicy {
        public RetryPolicy(int timeout, int maxAttempts) {}
    }
    public class Usage {
        public void Run() {
            var p1 = new RetryPolicy(0, 5);
            var p2 = new RetryPolicy(timeout: 0, maxAttempts: 5);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX004").Should().BeEmpty();
    }

    [Fact]
    public async Task AddOutbox_Without_Arguments_Should_Not_Report_Diagnostic()
    {
        var source = @"
namespace Microsoft.Extensions.DependencyInjection { public interface IServiceCollection {} }
namespace EricksonLopez.Outbox {
    public static class OutboxServiceCollectionExtensions {
        public static void AddOutbox(this Microsoft.Extensions.DependencyInjection.IServiceCollection services) {}
    }
}
namespace Test {
    public class Startup {
        public void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services) {
            services.AddOutbox();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().BeEmpty();
    }

    [Fact]
    public async Task Outbox_Other_Method_With_Null_Arg_Should_Not_Report_OUTBOX007()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        Task OtherMethod(object arg);
    }
}
namespace Test {
    public class Usage {
        public async Task Run(EricksonLopez.Outbox.IOutbox outbox) {
            await outbox.OtherMethod(null);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX007").Should().BeEmpty();
    }

    [Fact]
    public async Task StoreAsync_With_Null_Message_And_Active_Transaction_Should_Not_Report_OUTBOX007()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        Task StoreAsync<T>(T message, object transaction = null) where T : class;
    }
}
namespace Test {
    public class Usage {
        public async Task Run(EricksonLopez.Outbox.IOutbox outbox, object activeTx) {
            await outbox.StoreAsync<string>(null, activeTx);
            await outbox.StoreAsync<string>(null, transaction: activeTx);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX007").Should().BeEmpty();
    }

    [Fact]
    public async Task BrokerPublisher_Interface_And_Abstract_Class_Should_Not_Throw_Or_Report()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public struct DispatchResult {}
    public interface IBrokerPublisher {
        ValueTask<DispatchResult> PublishRawAsync(string topic, byte[] payload);
    }
}
namespace Test {
    public interface ICustomPublisher : EricksonLopez.Outbox.IBrokerPublisher {}
    public abstract class AbstractPublisher : EricksonLopez.Outbox.IBrokerPublisher {
        public abstract ValueTask<EricksonLopez.Outbox.DispatchResult> PublishRawAsync(string topic, byte[] payload);
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX010").Should().BeEmpty();
    }

    [Fact]
    public async Task StoreAsync_With_Named_Null_Transaction_At_Position_Zero_Should_Report_OUTBOX007()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        Task StoreAsync<T>(T message, object transaction = null);
    }
}
namespace Test {
    public class Msg { public Guid Id { get; set; } }
    public class Usage {
        public async Task Run(EricksonLopez.Outbox.IOutbox outbox) {
            await outbox.StoreAsync(transaction: null, message: new Msg());
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX007").Should().Be(1);
    }

    [Fact]
    public async Task AddOutbox_With_Direct_Identifier_Serializer_Call_Should_Recognize_Serializer()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class OutboxOptions {}
    public static class OutboxExtensions {
        public static void AddOutbox(System.Action<OutboxOptions> configure) {}
    }
}
namespace Test {
    public class Usage {
        public static void UseSerializer() {}
        public void Configure() {
            EricksonLopez.Outbox.OutboxExtensions.AddOutbox(options => {
                UseSerializer();
            });
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX005").Should().BeEmpty();
    }

    [Fact]
    public async Task StoreAsync_With_Positional_Index_Zero_Transaction_Param_Should_Report_OUTBOX007()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        Task StoreAsync<T>(object transaction, T message);
    }
}
namespace Test {
    public class Msg { public Guid Id { get; set; } }
    public class Usage {
        public async Task Run(EricksonLopez.Outbox.IOutbox outbox) {
            await outbox.StoreAsync(null, new Msg());
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX007").Should().Be(1);
    }

    [Fact]
    public async Task ExpressionStatement_With_Void_Type_Should_Not_Report_OUTBOX008()
    {
        var source = @"
namespace Test {
    public class Usage {
        public void VoidMethod() {}
        public void Run() {
            VoidMethod();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX008").Should().BeEmpty();
    }

    [Fact]
    public async Task AddOutbox_With_Non_Serializer_Method_Call_Should_Report_OUTBOX005()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class OutboxOptions {
        public void ConfigureOther() {}
    }
    public static class OutboxExtensions {
        public static void AddOutbox(System.Action<OutboxOptions> configure) {}
    }
}
namespace Test {
    public class Usage {
        public void Configure() {
            EricksonLopez.Outbox.OutboxExtensions.AddOutbox(options => {
                options.ConfigureOther();
            });
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX005").Should().Be(1);
    }

    [Fact]
    public void All_DiagnosticDescriptors_Should_Match_Exact_Specification()
    {
        // OUTBOX001
        var d1 = OutboxMessageAnalyzer.MissingIdRule;
        d1.Id.Should().Be("OUTBOX001");
        d1.Title.ToString().Should().Be("Message type missing 'Guid Id' property");
        d1.MessageFormat.ToString().Should().Be("Type '{0}' is missing a public 'Guid Id' property required for outbox identification.");
        d1.Category.Should().Be("Design");
        d1.DefaultSeverity.Should().Be(DiagnosticSeverity.Error);
        d1.IsEnabledByDefault.Should().BeTrue();
        d1.Description.ToString().Should().Be("Outbox messages must have an explicit 'Guid Id' property to guarantee unique delivery tracking.");

        // OUTBOX002
        var d2 = OutboxMessageAnalyzer.MissingAliasRule;
        d2.Id.Should().Be("OUTBOX002");
        d2.Title.ToString().Should().Be("Missing [OutboxMessage] attribute");
        d2.MessageFormat.ToString().Should().Be("Type '{0}' is missing the [OutboxMessage(\"alias\")] attribute. NativeAOT serialization will fail at runtime.");
        d2.Category.Should().Be("Usage");
        d2.DefaultSeverity.Should().Be(DiagnosticSeverity.Error);
        d2.IsEnabledByDefault.Should().BeTrue();
        d2.Description.ToString().Should().Be("All types stored via IOutbox<T>.StoreAsync must be decorated with [OutboxMessage(\"alias\")] to guarantee NativeAOT-safe, reflection-free serialization.");

        // OUTBOX003
        var d3 = OutboxMessageAnalyzer.NonIdempotentConsumerRule;
        d3.Id.Should().Be("OUTBOX003");
        d3.Title.ToString().Should().Be("Consumer is not idempotent");
        d3.MessageFormat.ToString().Should().Be("Type '{0}' handles messages but is not decorated with [InboxConsumer]. Without idempotency, duplicate messages will cause side-effect duplication.");
        d3.Category.Should().Be("Reliability");
        d3.DefaultSeverity.Should().Be(DiagnosticSeverity.Warning);
        d3.IsEnabledByDefault.Should().BeTrue();
        d3.Description.ToString().Should().Be("Consumers should implement the Inbox pattern via [InboxConsumer] to handle at-least-once delivery safely.");

        // OUTBOX004
        var d4 = OutboxMessageAnalyzer.InfiniteRetriesRule;
        d4.Id.Should().Be("OUTBOX004");
        d4.Title.ToString().Should().Be("Potentially infinite retry configuration");
        d4.MessageFormat.ToString().Should().Be("MaxAttempts = {0} detected. Values <= 0 or > 100 indicate a likely misconfiguration that can cause message queue buildup.");
        d4.Category.Should().Be("Configuration");
        d4.DefaultSeverity.Should().Be(DiagnosticSeverity.Warning);
        d4.IsEnabledByDefault.Should().BeTrue();
        d4.Description.ToString().Should().Be("Configure MaxAttempts to a reasonable finite value (1-50) to prevent infinite retry loops.");

        // OUTBOX005
        var d5 = OutboxMessageAnalyzer.SerializationConfigRule;
        d5.Id.Should().Be("OUTBOX005");
        d5.Title.ToString().Should().Be("Missing serializer in Outbox options");
        d5.MessageFormat.ToString().Should().Be("No configured JsonSerializerContext was found.");
        d5.Category.Should().Be("Configuration");
        d5.DefaultSeverity.Should().Be(DiagnosticSeverity.Error);
        d5.IsEnabledByDefault.Should().BeTrue();
        d5.Description.ToString().Should().Be("Configure a source-generated or AOT-safe serializer on OutboxOptions.");

        // OUTBOX013
        var d13 = OutboxMessageAnalyzer.MissingJsonSerializableRule;
        d13.Id.Should().Be("OUTBOX013");
        d13.Title.ToString().Should().Be("Message type not registered for AOT JSON serialization");
        d13.MessageFormat.ToString().Should().Be("The message type '{0}' is not registered using [JsonSerializable] in the JsonSerializerContext.");
        d13.Category.Should().Be("Configuration");
        d13.DefaultSeverity.Should().Be(DiagnosticSeverity.Error);
        d13.IsEnabledByDefault.Should().BeTrue();
        d13.Description.ToString().Should().Be("All messages must be registered with [JsonSerializable] in your JsonSerializerContext for NativeAOT support.");

        // OUTBOX006
        var d6 = OutboxMessageAnalyzer.MissingOutboxMessageAttributeRule;
        d6.Id.Should().Be("OUTBOX006");
        d6.Title.ToString().Should().Be("IIntegrationEvent implementer missing [OutboxMessage] attribute");
        d6.MessageFormat.ToString().Should().Be("Type '{0}' implements IIntegrationEvent but is missing [OutboxMessage(\"alias\")]. The NativeAOT message type resolver will throw KeyNotFoundException at runtime.");
        d6.Category.Should().Be("Usage");
        d6.DefaultSeverity.Should().Be(DiagnosticSeverity.Warning);
        d6.IsEnabledByDefault.Should().BeTrue();
        d6.Description.ToString().Should().Be("All types that implement IIntegrationEvent and are stored via the Outbox must be decorated with [OutboxMessage(\"alias\")] to guarantee that the source-generated type resolver (NativeAOT-safe) can serialize and deserialize them.");

        // OUTBOX007
        var d7 = OutboxMessageAnalyzer.NullTransactionRule;
        d7.Id.Should().Be("OUTBOX007");
        d7.Title.ToString().Should().Be("StoreAsync called without a transaction");
        d7.MessageFormat.ToString().Should().Be("StoreAsync is called with a null transaction. The Transactional Outbox pattern requires the outbox write to be part of the same DB transaction as the business operation. Pass the active transaction to guarantee atomicity.");
        d7.Category.Should().Be("Reliability");
        d7.DefaultSeverity.Should().Be(DiagnosticSeverity.Warning);
        d7.IsEnabledByDefault.Should().BeTrue();
        d7.Description.ToString().Should().Be("Outbox writes must be transactional. If StoreAsync receives null, messages may be persisted without the accompanying business transaction, violating the exactly-once guarantee.");

        // OUTBOX008
        var d8 = OutboxMessageAnalyzer.AbandonedBuilderRule;
        d8.Id.Should().Be("OUTBOX008");
        d8.Title.ToString().Should().Be("Outbox message builder abandoned");
        d8.MessageFormat.ToString().Should().Be("The outbox message builder was abandoned without calling StoreAsync. The message will not be saved.");
        d8.Category.Should().Be("Usage");
        d8.DefaultSeverity.Should().Be(DiagnosticSeverity.Error);
        d8.IsEnabledByDefault.Should().BeTrue();
        d8.Description.ToString().Should().Be("Always call StoreAsync(transaction) at the end of the IOutboxMessageBuilder chain.");

        // OUTBOX009
        var d9 = OutboxMessageAnalyzer.ZeroMaxRetriesRule;
        d9.Id.Should().Be("OUTBOX009");
        d9.Title.ToString().Should().Be("MaxRetryCount set to 0");
        d9.MessageFormat.ToString().Should().Be("MaxRetryCount is set to 0. All failing messages will be immediately dead-lettered without any retries.");
        d9.Category.Should().Be("Configuration");
        d9.DefaultSeverity.Should().Be(DiagnosticSeverity.Warning);
        d9.IsEnabledByDefault.Should().BeTrue();
        d9.Description.ToString().Should().Be("Setting MaxRetryCount to 0 disables the transient fault tolerance mechanism of the outbox pattern.");

        // OUTBOX012
        var d12 = OutboxMessageAnalyzer.DefaultDispatchResultRule;
        d12.Id.Should().Be("OUTBOX012");
        d12.Title.ToString().Should().Be("IBrokerPublisher returns default(DispatchResult)");
        d12.MessageFormat.ToString().Should().Be("'{0}.PublishRawAsync' returns 'default' which is an invalid DispatchResult state (Success=false, ShouldRetry=false, Error=null). Use DispatchResult.Ok(), DispatchResult.FailAndRetry(ex), or DispatchResult.FailFatal(ex) instead.");
        d12.Category.Should().Be("Reliability");
        d12.DefaultSeverity.Should().Be(DiagnosticSeverity.Error);
        d12.IsEnabledByDefault.Should().BeTrue();
        d12.Description.ToString().Should().Be("Returning default(DispatchResult) from IBrokerPublisher.PublishRawAsync causes the dispatcher to dead-letter the message with no error context. Always return a valid DispatchResult factory result.");

        // TransactionRequiredAnalyzer Rule OUTBOX010
        var txAnalyzer = new TransactionRequiredAnalyzer();
        txAnalyzer.SupportedDiagnostics.Length.Should().Be(1);
        var d10 = txAnalyzer.SupportedDiagnostics[0];
        d10.Id.Should().Be("OUTBOX010");
        d10.Title.ToString().Should().Be("StoreAsync called without a transaction in the fluent builder");
        d10.MessageFormat.ToString().Should().Be("The outbox message is being saved without a transaction. You must call .WithTransaction(...) before .StoreAsync().");
        d10.Category.Should().Be("Usage");
        d10.DefaultSeverity.Should().Be(DiagnosticSeverity.Error);
        d10.IsEnabledByDefault.Should().BeTrue();
        d10.Description.ToString().Should().Be("The Transactional Outbox pattern requires messages to be saved within the same database transaction as the business data. Calling StoreAsync() on the builder without first calling WithTransaction() will throw at runtime.");
    }

    [Fact]
    public async Task AbandonedBuilder_With_Different_Builder_Types_Should_Report_OUTBOX008()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class OutboxOptionsBuilder {
        public static OutboxOptionsBuilder Create() => new OutboxOptionsBuilder();
    }
}
namespace Test {
    public class Usage {
        public void Run() {
            EricksonLopez.Outbox.OutboxOptionsBuilder.Create();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX008").Should().Be(1);
    }

    [Fact]
    public async Task AddOutbox_With_UseGeneratedTypes_And_Serialization_Should_Not_Report_OUTBOX005()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class OutboxOptions {
        public void UseGeneratedTypes() {}
        public void UseGeneratedTypesAndSerialization() {}
    }
    public static class OutboxExtensions {
        public static void AddOutbox(System.Action<OutboxOptions> configure) {}
    }
}
namespace Test {
    public class Usage {
        public void Run1() {
            EricksonLopez.Outbox.OutboxExtensions.AddOutbox(options => options.UseGeneratedTypes());
        }
        public void Run2() {
            EricksonLopez.Outbox.OutboxExtensions.AddOutbox(options => options.UseGeneratedTypesAndSerialization());
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX005").Should().BeEmpty();
    }

    [Fact]
    public async Task BrokerPublisher_With_Default_DispatchResult_Should_Report_OUTBOX012()
    {
        var source = @"
using System.Threading.Tasks;
namespace EricksonLopez.Outbox {
    public struct DispatchResult {}
    public interface IBrokerPublisher {
        ValueTask<DispatchResult> PublishRawAsync(string topic, byte[] payload);
    }
}
namespace Test {
    public class MyPublisher1 : EricksonLopez.Outbox.IBrokerPublisher {
        public ValueTask<EricksonLopez.Outbox.DispatchResult> PublishRawAsync(string topic, byte[] payload) {
            return default;
        }
    }
    public class MyPublisher2 : EricksonLopez.Outbox.IBrokerPublisher {
        public ValueTask<EricksonLopez.Outbox.DispatchResult> PublishRawAsync(string topic, byte[] payload) {
            return default(EricksonLopez.Outbox.DispatchResult);
        }
    }
    public class MyPublisher3 : EricksonLopez.Outbox.IBrokerPublisher {
        public ValueTask<EricksonLopez.Outbox.DispatchResult> OtherMethod() {
            return default;
        }
        public ValueTask<EricksonLopez.Outbox.DispatchResult> PublishRawAsync(string topic, byte[] payload) {
            return new ValueTask<EricksonLopez.Outbox.DispatchResult>(new EricksonLopez.Outbox.DispatchResult());
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        var outbox12Diags = diags.Where(d => d.Id == "OUTBOX012").ToList();
        outbox12Diags.Count.Should().Be(2);
        var messages = outbox12Diags.Select(d => d.GetMessage()).ToList();
        messages.Should().Contain("'MyPublisher1.PublishRawAsync' returns 'default' which is an invalid DispatchResult state (Success=false, ShouldRetry=false, Error=null). Use DispatchResult.Ok(), DispatchResult.FailAndRetry(ex), or DispatchResult.FailFatal(ex) instead.");
        messages.Should().Contain("'MyPublisher2.PublishRawAsync' returns 'default' which is an invalid DispatchResult state (Success=false, ShouldRetry=false, Error=null). Use DispatchResult.Ok(), DispatchResult.FailAndRetry(ex), or DispatchResult.FailFatal(ex) instead.");
    }

    [Fact]
    public async Task Consumer_Interface_And_Attribute_Variations_Should_Be_Handled()
    {
        var source = @"
namespace EricksonLopez.Outbox.Contracts {
    public class InboxConsumerAttribute : System.Attribute {}
    public class IdempotentConsumerAttribute : System.Attribute {}
}
namespace Test {
    public interface IHandleMessages<T> {}
    public interface IMessageHandler<T> {}
    public interface IConsumer<T> {}

    [EricksonLopez.Outbox.Contracts.IdempotentConsumer]
    public class ConsumerA : IHandleMessages<string> {}

    [EricksonLopez.Outbox.Contracts.InboxConsumer]
    public class ConsumerB : IMessageHandler<string> {}

    public class ConsumerC : IConsumer<string> {}
}";
        var diags = await GetDiagnosticsAsync(source);
        var outbox03Diags = diags.Where(d => d.Id == "OUTBOX003").ToList();
        outbox03Diags.Count.Should().Be(1);
        outbox03Diags[0].GetMessage().Should().Be("Type 'ConsumerC' handles messages but is not decorated with [InboxConsumer]. Without idempotency, duplicate messages will cause side-effect duplication.");
    }

    [Fact]
    public async Task RetryPolicy_With_Multiple_Args_Should_Report_OUTBOX004()
    {
        var source = @"
namespace Test {
    public class CustomRetryPolicy {
        public CustomRetryPolicy(string name, int maxAttempts) {}
    }
    public class Usage {
        public void Run() {
            _ = new CustomRetryPolicy(""custom"", 0);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX004").Should().Be(1);
    }

    [Fact]
    public async Task StoreAsync_With_DbTransaction_Types_Should_Report_OUTBOX007()
    {
        var source = @"
namespace System.Data.Common { public abstract class DbTransaction {} }
namespace System.Data { public interface IDbTransaction {} }
namespace EricksonLopez.Outbox {
    public interface IOutboxTransactionContext {}
    public interface IOutbox {
        System.Threading.Tasks.Task StoreAsync<T>(T msg, IOutboxTransactionContext transactionContext);
    }
}
namespace Test {
    public class MyMsg { public System.Guid Id { get; set; } }
    public class Usage {
        public async System.Threading.Tasks.Task Run(EricksonLopez.Outbox.IOutbox outbox) {
            await outbox.StoreAsync(new MyMsg(), null);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX007").Should().Be(1);
    }

    [Fact]
    public async Task JsonSerializerContext_With_Partial_Registrations_Should_Report_Only_Missing()
    {
        var source = @"
namespace System.Text.Json.Serialization {
    public abstract class JsonSerializerContext {}
    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
    public class JsonSerializableAttribute : System.Attribute {
        public JsonSerializableAttribute(System.Type t) {}
    }
}
namespace EricksonLopez.Outbox.Contracts { public class OutboxMessageAttribute : System.Attribute {} }
namespace Test {
    [EricksonLopez.Outbox.Contracts.OutboxMessage]
    public class RegisteredMsg { public System.Guid Id { get; set; } }

    [EricksonLopez.Outbox.Contracts.OutboxMessage]
    public class UnregisteredMsg { public System.Guid Id { get; set; } }

    [System.Text.Json.Serialization.JsonSerializable(typeof(RegisteredMsg))]
    public partial class MyJsonContext : System.Text.Json.Serialization.JsonSerializerContext {}
}";
        var diags = await GetDiagnosticsAsync(source);
        var jsonDiags = diags.Where(d => d.Id == "OUTBOX013").ToList();
        jsonDiags.Count.Should().Be(1);
        jsonDiags[0].GetMessage().Should().Be("The message type 'UnregisteredMsg' is not registered using [JsonSerializable] in the JsonSerializerContext.");
    }

    [Fact]
    public async Task TransactionRequired_With_WithTransaction_Should_Not_Report_OUTBOX010()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class OutboxMessageBuilder {
        public OutboxMessageBuilder WithTransaction(object tx) => this;
        public OutboxMessageBuilder WithHeader(string key, string val) => this;
        public void StoreAsync() {}
    }
}
namespace Test {
    public class Usage {
        public void Run(EricksonLopez.Outbox.OutboxMessageBuilder builder) {
            builder.WithHeader(""k"", ""v"").WithTransaction(null).StoreAsync();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX010").Should().BeEmpty();
    }

    [Fact]
    public async Task TransactionRequired_Without_WithTransaction_Should_Report_OUTBOX010()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class OutboxMessageBuilder {
        public OutboxMessageBuilder WithHeader(string key, string val) => this;
        public void StoreAsync() {}
    }
}
namespace Test {
    public class Usage {
        public void Run(EricksonLopez.Outbox.OutboxMessageBuilder builder) {
            builder.WithHeader(""k"", ""v"").StoreAsync();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        var txDiags = diags.Where(d => d.Id == "OUTBOX010").ToList();
        txDiags.Count.Should().Be(1);
        txDiags[0].GetMessage().Should().Be("The outbox message is being saved without a transaction. You must call .WithTransaction(...) before .StoreAsync().");
        txDiags[0].Severity.Should().Be(DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task TransactionRequired_With_Plain_MemberAccess_Should_Report_OUTBOX010()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class OutboxMessageBuilder {
        public void StoreAsync() {}
    }
}
namespace Test {
    public class Holder {
        public EricksonLopez.Outbox.OutboxMessageBuilder Builder { get; set; }
    }
    public class Usage {
        public void Run(Holder holder) {
            holder.Builder.StoreAsync();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX010").Should().Be(1);
    }

    [Fact]
    public async Task TransactionRequired_With_Other_Namespace_Or_Type_Should_Not_Report_OUTBOX010()
    {
        var source = @"
namespace Other.Outbox {
    public class OutboxMessageBuilder {
        public void StoreAsync() {}
    }
}
namespace EricksonLopez.Outbox {
    public class OtherBuilder {
        public void StoreAsync() {}
    }
}
namespace Test {
    public class Usage {
        public void Run(Other.Outbox.OutboxMessageBuilder b1, EricksonLopez.Outbox.OtherBuilder b2) {
            b1.StoreAsync();
            b2.StoreAsync();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX010").Should().BeEmpty();
    }

    [Fact]
    public async Task TransactionRequired_With_Deep_Chained_WithTransaction_Should_Not_Report_OUTBOX010()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class OutboxMessageBuilder {
        public OutboxMessageBuilder WithHeader(string key, string val) => this;
        public OutboxMessageBuilder WithTransaction(object tx) => this;
        public void StoreAsync() {}
    }
}
namespace Test {
    public class Usage {
        public void Run(EricksonLopez.Outbox.OutboxMessageBuilder builder) {
            builder.WithHeader(""k1"", ""v1"").WithTransaction(null).WithHeader(""k2"", ""v2"").StoreAsync();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX010").Should().BeEmpty();
    }

    [Fact]
    public async Task TransactionRequired_With_MultiLevel_Plain_MemberAccess_Should_Report_OUTBOX010()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class OutboxMessageBuilder {
        public void StoreAsync() {}
    }
}
namespace Test {
    public class Sub {
        public EricksonLopez.Outbox.OutboxMessageBuilder Builder { get; set; }
    }
    public class Container {
        public Sub SubHolder { get; set; }
    }
    public class Usage {
        public void Run(Container container) {
            container.SubHolder.Builder.StoreAsync();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX010").Should().Be(1);
    }

    [Fact]
    public async Task TransactionRequired_With_Non_MemberAccess_Invocation_Expression_Should_Report_OUTBOX010()
    {
        var source = @"
using System;
namespace EricksonLopez.Outbox {
    public class OutboxMessageBuilder {
        public void StoreAsync() {}
    }
}
namespace Test {
    public class Usage {
        public void Run(Func<EricksonLopez.Outbox.OutboxMessageBuilder> getBuilder) {
            getBuilder().StoreAsync();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX010").Should().Be(1);
    }

    [Fact]
    public void TransactionRequiredAnalyzer_Initialize_Should_Configure_AnalysisContext()
    {
        var analyzer = new TransactionRequiredAnalyzer();
        var context = new TestAnalysisContext();
        analyzer.Initialize(context);

        context.ConcurrentExecutionEnabled.Should().BeTrue();
        context.GeneratedCodeAnalysisConfigured.Should().BeTrue();
        context.GeneratedCodeAnalysisFlags.Should().Be(GeneratedCodeAnalysisFlags.None);
        context.RegisteredSyntaxNodeActions.Should().Contain(SyntaxKind.InvocationExpression);
    }

    [Fact]
    public void OutboxMessageAnalyzer_Initialize_Should_Configure_AnalysisContext()
    {
        var analyzer = new OutboxMessageAnalyzer();
        var context = new TestAnalysisContext();
        analyzer.Initialize(context);

        context.ConcurrentExecutionEnabled.Should().BeTrue();
        context.GeneratedCodeAnalysisConfigured.Should().BeTrue();
        context.GeneratedCodeAnalysisFlags.Should().Be(GeneratedCodeAnalysisFlags.None);
        context.RegisteredSyntaxNodeActions.Should().Contain(SyntaxKind.InvocationExpression);
        context.RegisteredSyntaxNodeActions.Should().Contain(SyntaxKind.ObjectCreationExpression);
        context.RegisteredSymbolKinds.Should().Contain(SymbolKind.NamedType);
        context.RegisteredOperationKinds.Should().Contain(OperationKind.ExpressionStatement);
        context.RegisteredOperationKinds.Should().Contain(OperationKind.SimpleAssignment);
        context.RegisteredSymbolKinds.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RetryPolicy_Without_ArgumentList_Should_Not_Crash()
    {
        var source = @"
namespace Test {
    public class RetryPolicy {
        public int MaxAttempts { get; set; }
    }
    public class Usage {
        public void Run() {
            var policy = new RetryPolicy { MaxAttempts = 3 };
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().BeEmpty();
    }

    [Fact]
    public async Task AddOutbox_Without_Arguments_Should_Not_Report_OUTBOX005()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public static class Extensions {
        public static void AddOutbox(this object services) {}
    }
}
namespace Test {
    using EricksonLopez.Outbox;
    public class Usage {
        public void Run(object services) {
            services.AddOutbox();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().BeEmpty();
    }

    [Fact]
    public async Task Foreign_IOutbox_StoreAsync_Should_Not_Trigger_Diagnostics()
    {
        var source = @"
namespace OtherCompany.Outbox {
    public interface IOutbox {
        void StoreAsync<T>(T msg);
    }
}
namespace Test {
    public class ForeignMessage {}
    public class Usage {
        public void Run(OtherCompany.Outbox.IOutbox outbox, ForeignMessage msg) {
            outbox.StoreAsync(msg);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id is "OUTBOX001" or "OUTBOX002").Should().BeEmpty();
    }

    [Fact]
    public async Task Consumers_Implementing_IHandleMessages_And_IMessageHandler_Should_Be_Analyzed()
    {
        var source = @"
namespace Test {
    public interface IHandleMessages<T> {}
    public interface IMessageHandler<T> {}
    public class MyMsgHandler1 : IHandleMessages<object> {}
    public class MyMsgHandler2 : IMessageHandler<object> {}
    [EricksonLopez.Outbox.InboxConsumer]
    public class MyDecoratedHandler1 : IHandleMessages<object> {}
    [EricksonLopez.Outbox.IdempotentConsumer]
    public class MyDecoratedHandler2 : IMessageHandler<object> {}
}";
        var diags = await GetDiagnosticsAsync(source);
        var consumerDiags = diags.Where(d => d.Id == "OUTBOX003").ToList();
        consumerDiags.Should().HaveCount(2);
        consumerDiags.Select(d => d.GetMessage()).Should().Contain(m => m.Contains("MyMsgHandler1"));
        consumerDiags.Select(d => d.GetMessage()).Should().Contain(m => m.Contains("MyMsgHandler2"));
    }

    [Fact]
    public async Task StoreAsync_With_Named_And_Typed_Null_Transactions_Should_Report_OUTBOX007()
    {
        var source = @"
using System;
using System.Data.Common;
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        void StoreAsync<T>(T msg, object tx);
        void StoreAsync<T>(T msg, string context);
        void StoreAsync<T>(T msg, DbTransaction transactionContext);
    }
    [OutboxMessage(""alias"")]
    public class ValidMessage { public Guid Id { get; } }
}
namespace Test {
    using EricksonLopez.Outbox;
    public class Usage {
        public void Run(IOutbox outbox, ValidMessage msg) {
            outbox.StoreAsync(msg, tx: null);
            outbox.StoreAsync(msg, context: null);
            outbox.StoreAsync(msg, transactionContext: null);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        var nullTxDiags = diags.Where(d => d.Id == "OUTBOX007").ToList();
        nullTxDiags.Should().HaveCount(3);
    }

    [Fact]
    public async Task AbandonedBuilder_For_Various_Builders_Should_Report_OUTBOX008()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class OutboxOptionsBuilder {}
    public class OutboxPipelineBuilder {}
    public interface IOutboxBuilder {}
    public static class Factory {
        public static OutboxOptionsBuilder CreateOptions() => new();
        public static OutboxPipelineBuilder CreatePipeline() => new();
        public static IOutboxBuilder CreateOutbox() => null!;
        public static void DoVoid() {}
    }
}
namespace Test {
    using EricksonLopez.Outbox;
    public class Usage {
        public void Run() {
            Factory.CreateOptions();
            Factory.CreatePipeline();
            Factory.CreateOutbox();
            Factory.DoVoid();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        var builderDiags = diags.Where(d => d.Id == "OUTBOX008").ToList();
        builderDiags.Should().HaveCount(3);
    }

    [Fact]
    public async Task BrokerPublisher_Interface_Abstract_And_Throwing_Should_Not_Report_OUTBOX012()
    {
        var source = @"
using System;
using System.Threading.Tasks;
namespace EricksonLopez.Outbox {
    public struct DispatchResult {}
    public interface IBrokerPublisher {
        Task<DispatchResult> PublishRawAsync(string topic, byte[] data);
    }
    public interface ICustomPublisher : IBrokerPublisher {}
    public abstract class AbstractPublisher : IBrokerPublisher {
        public abstract Task<DispatchResult> PublishRawAsync(string topic, byte[] data);
    }
    public class ThrowingPublisher : IBrokerPublisher {
        public Task<DispatchResult> PublishRawAsync(string topic, byte[] data) {
            throw new NotImplementedException();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX012").Should().BeEmpty();
    }

    [Fact]
    public async Task JsonSerializerContext_With_Nested_Namespaces_Should_Be_Scanned()
    {
        var source = @"
using System;
using System.Text.Json.Serialization;
namespace Nested.A.B.C {
    [EricksonLopez.Outbox.OutboxMessage(""nested_msg"")]
    public class NestedMsg { public Guid Id { get; } }

    [JsonSerializable(typeof(NestedMsg))]
    public partial class NestedContext : JsonSerializerContext {}
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX013").Should().BeEmpty();
    }

    [Fact]
    public async Task AddOutbox_With_Multiple_Configurations_And_Non_Lambda_Should_Work()
    {
        var source = @"
using System;
namespace EricksonLopez.Outbox {
    public class OutboxOptions {
        public void UseSerializer() {}
        public void UseGeneratedTypes() {}
    }
    public static class Extensions {
        public static void AddOutbox(this object services, Action<OutboxOptions> configure) {}
        public static void AddOutbox(this object services, OutboxOptions options) {}
    }
}
namespace Test {
    using EricksonLopez.Outbox;
    public class Usage {
        public void Run(object services) {
            services.AddOutbox(opt => {
                opt.UseSerializer();
                opt.UseGeneratedTypes();
            });
            services.AddOutbox(new OutboxOptions());
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX005").Should().BeEmpty();
    }

    [Fact]
    public async Task OutboxMessage_Custom_Attribute_Class_Without_Attribute_Suffix_Should_Be_Recognized()
    {
        var source = @"
using System;
namespace EricksonLopez.Outbox {
    [AttributeUsage(AttributeTargets.Class)]
    public class OutboxMessage : Attribute {
        public OutboxMessage(string alias) {}
    }
    public interface IOutbox {
        void StoreAsync<T>(T msg);
    }
    public interface IIntegrationEvent {}
}
namespace Test {
    using System;
    using EricksonLopez.Outbox;

    [OutboxMessage(""custom_alias"")]
    public class CustomEvent : IIntegrationEvent {
        public Guid Id { get; set; }
    }

    public class Usage {
        public void Run(IOutbox outbox, CustomEvent evt) {
            outbox.StoreAsync(evt);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id is "OUTBOX001" or "OUTBOX002" or "OUTBOX006").Should().BeEmpty();
    }

    [Fact]
    public async Task RetryPolicy_Constructor_EdgeCases_Should_Be_Handled()
    {
        var source = @"
namespace Test {
    public class RetryPolicy {
        public RetryPolicy(int maxAttempts) {}
        public RetryPolicy(int maxAttempts, string name) {}
    }
    public class Usage {
        public void Run() {
            var p1 = new RetryPolicy(5, ""linear"");
            var p2 = new RetryPolicy(0, ""bad"");
            var p3 = new RetryPolicy(150, ""too_high"");
            var p4 = new RetryPolicy(maxAttempts: -1);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        var retriesDiags = diags.Where(d => d.Id == "OUTBOX004").ToList();
        retriesDiags.Should().HaveCount(3);
    }

    [Fact]
    public async Task AddOutbox_Lambda_Variations_Should_Be_Analyzed()
    {
        var source = @"
using System;
namespace EricksonLopez.Outbox {
    public class OutboxOptions {
        public void UseGeneratedTypesAndSerialization() {}
    }
    public static class Extensions {
        public static void AddOutbox(this object services, Action<OutboxOptions> configure) {}
        public static void AddOther(this object services, Action<OutboxOptions> configure) {}
    }
}
namespace Test {
    using EricksonLopez.Outbox;
    public class Usage {
        public void Run(object services) {
            services.AddOutbox((opt) => opt.UseGeneratedTypesAndSerialization());
            services.AddOutbox((opt) => { opt.UseGeneratedTypesAndSerialization(); });
            services.AddOther(opt => {});
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX005").Should().BeEmpty();
    }

    [Fact]
    public async Task AbandonedBuilder_IOutboxMessageBuilder_Should_Report_OUTBOX008()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutboxMessageBuilder {}
    public static class Factory {
        public static IOutboxMessageBuilder Create() => null!;
    }
}
namespace Test {
    using EricksonLopez.Outbox;
    public class Usage {
        public void Run() {
            Factory.Create();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        var builderDiags = diags.Where(d => d.Id == "OUTBOX008").ToList();
        builderDiags.Should().HaveCount(1);
    }

    [Fact]
    public async Task BrokerPublisher_With_Void_Local_Function_Should_Not_Crash()
    {
        var source = @"
using System;
using System.Threading.Tasks;
namespace EricksonLopez.Outbox {
    public struct DispatchResult {
        public static DispatchResult Ok() => default;
    }
    public interface IBrokerPublisher {
        Task<DispatchResult> PublishRawAsync(string topic, byte[] data);
    }
    public class ValidPublisher : IBrokerPublisher {
        public Task<DispatchResult> PublishRawAsync(string topic, byte[] data) {
            void Helper() { return; }
            Helper();
            return Task.FromResult(DispatchResult.Ok());
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX012").Should().BeEmpty();
    }

    [Fact]
    public async Task StoreAsync_All_Named_And_Typed_Variants_Should_Be_Covered()
    {
        var source = @"
using System;
using System.Data;
using System.Data.Common;
namespace EricksonLopez.Outbox {
    public interface IOutboxTransactionContext {}
    public interface IOutbox {
        void StoreAsync<T>(T msg, object tx = null, object transactionContext = null, object context = null, object transaction = null);
        void OtherMethod(object arg);
    }
}
namespace OtherCompany.Outbox {
    public interface IOutbox {
        void StoreAsync<T>(T msg, object tx);
    }
}
namespace Test {
    using EricksonLopez.Outbox;
    public class Usage {
        public void Run(IOutbox outbox, OtherCompany.Outbox.IOutbox foreignOutbox) {
            outbox.StoreAsync(1, tx: null);
            outbox.StoreAsync(1, transactionContext: null);
            outbox.StoreAsync(1, context: null);
            outbox.StoreAsync(1, transaction: null);
            outbox.OtherMethod(null);
            foreignOutbox.StoreAsync(1, null);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        var nullTxDiags = diags.Where(d => d.Id == "OUTBOX007").ToList();
        nullTxDiags.Should().HaveCount(4);
    }

    [Fact]
    public async Task AbandonedBuilder_Void_Statement_Should_Not_Report()
    {
        var source = @"
using System;
namespace Test {
    public class Usage {
        public void Run() {
            Console.WriteLine(""hello"");
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX008").Should().BeEmpty();
    }

    [Fact]
    public async Task JsonSerializerContext_Deeply_Nested_Namespaces_Should_Be_Analyzed()
    {
        var source = @"
using System;
using System.Text.Json.Serialization;
namespace L1.L2.L3 {
    [EricksonLopez.Outbox.OutboxMessage(""deep_msg"")]
    public class DeepMsg { public Guid Id { get; set; } }

    [JsonSerializable(typeof(DeepMsg))]
    public partial class DeepContext : JsonSerializerContext {}
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX013").Should().BeEmpty();
    }

    [Fact]
    public async Task StoreAsync_Positional_Parameters_With_Various_Types_And_Positions()
    {
        var source = @"
using System;
using System.Data;
using System.Data.Common;
namespace EricksonLopez.Outbox {
    public interface IOutboxTransactionContext {}
    public interface IOutbox {
        void StoreAsync<T>(IOutboxTransactionContext ctx, T msg);
        void StoreAsync<T>(DbTransaction tx, T msg, string dummy = null);
        void StoreAsync<T>(IDbTransaction tx, T msg, int dummy = 0);
        void StoreAsync<T>(T msg, int partition, IOutboxTransactionContext ctx);
    }
}
namespace Test {
    using System;
    using System.Data.Common;
    using EricksonLopez.Outbox;
    public class Usage {
        public void Run(IOutbox outbox) {
            outbox.StoreAsync((IOutboxTransactionContext)null, 1);
            outbox.StoreAsync(1, 0, (IOutboxTransactionContext)null);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        var nullTxDiags = diags.Where(d => d.Id == "OUTBOX007").ToList();
        nullTxDiags.Should().HaveCount(2);
    }

    [Fact]
    public async Task RetryPolicy_Named_Arguments_Out_Of_Order_And_Excess_Arguments_Should_Be_Analyzed()
    {
        var source = @"
namespace Test {
    public class RetryPolicy {
        public RetryPolicy(int maxAttempts, string name) {}
    }
    public class Usage {
        public void Run() {
            var p1 = new RetryPolicy(name: ""custom"", maxAttempts: 0);
            var p2 = new RetryPolicy(name: ""custom"", maxAttempts: 150);
            var p3 = new RetryPolicy(5, ""valid"", 999);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().OnlyContain(d => d.Id == "OUTBOX004");
        diags.Should().HaveCount(2);
    }

    [Fact]
    public async Task StoreAsync_All_Positional_Parameter_Names_And_Types_At_Index_0()
    {
        var source = @"
using System;
using System.Data;
using System.Data.Common;
namespace EricksonLopez.Outbox {
    public interface IOutboxTransactionContext {}
    public interface IOutbox {}
    public interface IOutboxA : IOutbox { void StoreAsync<T>(object transactionContext, T msg); }
    public interface IOutboxB : IOutbox { void StoreAsync<T>(object context, T msg); }
    public interface IOutboxC : IOutbox { void StoreAsync<T>(object tx, T msg); }
    public interface IOutboxD : IOutbox { void StoreAsync<T>(DbTransaction databaseTransaction, T msg); }
    public interface IOutboxE : IOutbox { void StoreAsync<T>(IDbTransaction databaseTransaction, T msg); }
    public interface IOutboxF : IOutbox { void StoreAsync<T>(T msg, IOutboxTransactionContext tx); }
    public interface IOutboxG : IOutbox { void StoreAsync<T>(IOutboxTransactionContext tx1, IOutboxTransactionContext tx2); }
    public interface IOutboxH : IOutbox { void StoreAsync<T>(T msg, int partition, IOutboxTransactionContext tx); }
}
namespace Test {
    using System;
    using System.Data;
    using System.Data.Common;
    using EricksonLopez.Outbox;
    public class Usage {
        public void Run(IOutboxA a, IOutboxB b, IOutboxC c, IOutboxD d, IOutboxE e, IOutboxF f, IOutboxG g, IOutboxH h, IOutboxTransactionContext validTx) {
            a.StoreAsync((object)null!, 1);
            b.StoreAsync((object)null!, 1);
            c.StoreAsync((object)null!, 1);
            d.StoreAsync((DbTransaction)null!, 1);
            e.StoreAsync((IDbTransaction)null!, 1);
            f.StoreAsync((object)null!, (IOutboxTransactionContext)null!); // index 0 is not tx, index 1 is tx -> 1 OUTBOX007
            g.StoreAsync<int>((IOutboxTransactionContext)null!, (IOutboxTransactionContext)null!); // index 0 and 1 are both tx -> only 1 OUTBOX007 due to early return
            h.StoreAsync((object)null!, 0, validTx); // index 0 is msg (null), index 1 is partition, index 2 is validTx -> NOT OUTBOX007
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        var nullTxDiags = diags.Where(d => d.Id == "OUTBOX007").ToList();
        nullTxDiags.Should().HaveCount(7);
    }

    [Fact]
    public async Task AbandonedBuilder_VoidMethod_In_Outbox_Namespace_Should_Not_Report()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class VoidService {
        public void Execute() {}
    }
}
namespace Test {
    using EricksonLopez.Outbox;
    public class Usage {
        public void Run(VoidService s) {
            s.Execute();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX008").Should().BeEmpty();
    }

    [Fact]
    public async Task JsonSerializerContext_With_Custom_OutboxMessage_Attribute_Class_And_Nested_Namespaces()
    {
        var source = @"
using System;
using System.Text.Json.Serialization;

namespace System.Text.Json.Serialization {
    public abstract class JsonSerializerContext {}
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class JsonSerializableAttribute : Attribute {
        public JsonSerializableAttribute(Type type) {}
    }
}

namespace EricksonLopez.Outbox {
    [AttributeUsage(AttributeTargets.Class)]
    public class OutboxMessageAttribute : Attribute {
        public OutboxMessageAttribute(string alias) {}
    }
    [AttributeUsage(AttributeTargets.Class)]
    public class OutboxMessage : Attribute {
        public OutboxMessage(string alias) {}
    }
}

namespace Level1.Level2.Level3 {
    using System;
    using System.Text.Json.Serialization;
    using EricksonLopez.Outbox;

    [OutboxMessage(""deep_custom_msg"")]
    public class DeepCustomMsg { public Guid Id { get; set; } }

    [JsonSerializable(typeof(DeepCustomMsg))]
    public partial class DeepCustomContext : JsonSerializerContext {}
}

namespace Unregistered.A.B.C {
    using System;
    using EricksonLopez.Outbox;

    [OutboxMessageAttribute(""unreg_msg"")]
    public class UnregisteredMsg : Exception { public Guid Id { get; set; } }

    [OutboxMessage(""unreg_msg2"")]
    public class UnregisteredMsg2 { public Guid Id { get; set; } }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().OnlyContain(d => d.Id == "OUTBOX013");
        var missingJsonDiags = diags.Where(d => d.Id == "OUTBOX013").ToList();
        missingJsonDiags.Should().HaveCount(2);
    }

    [Fact]
    public async Task OtherMethod_With_DbTransaction_Param_Should_Not_Report_OUTBOX007()
    {
        var source = @"
using System.Data.Common;
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        void OtherMethod(DbTransaction tx);
        void StoreAsync<T>(T msg, IOutboxTransactionContext tx);
    }
    public interface IOutboxTransactionContext {}
}
namespace Test {
    using System.Data.Common;
    using EricksonLopez.Outbox;
    public class Usage {
        public void Run(IOutbox outbox, IOutboxTransactionContext validTx) {
            outbox.OtherMethod((DbTransaction)null!);
            outbox.StoreAsync(1, validTx, (DbTransaction)null!); // 3rd arg is out of bounds
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id is "OUTBOX007" or "AD0001").Should().BeEmpty();
    }

    [Fact]
    public async Task StoreAsync_With_Null_Message_And_Valid_Transaction_Should_Not_Report_OUTBOX007()
    {
        var source = @"
using System;
namespace EricksonLopez.Outbox {
    public interface IOutboxTransactionContext {}
    public interface IOutbox {
        void StoreAsync<T>(T msg, int partition, IOutboxTransactionContext tx);
    }
}
namespace Test {
    using EricksonLopez.Outbox;
    public class Usage {
        public void Run(IOutbox outbox, IOutboxTransactionContext validTx) {
            outbox.StoreAsync((object)null!, 0, validTx);
            outbox.StoreAsync(1, 0, validTx, (object)null!); // extra 4th null parameter
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX007").Should().BeEmpty();
    }

    [Fact]
    public async Task BrokerPublisher_With_Default_Literal_And_Default_Expression_Returns()
    {
        var source = @"
using System;
using System.Threading.Tasks;
namespace EricksonLopez.Outbox {
    public struct DispatchResult {}
    public interface IBrokerPublisher {
        Task<DispatchResult> PublishRawAsync(string topic, byte[] data);
    }
    public class DefaultLiteralPublisher : IBrokerPublisher {
        public Task<DispatchResult> PublishRawAsync(string topic, byte[] data) {
            return default;
        }
    }
    public class DefaultExpressionPublisher : IBrokerPublisher {
        public Task<DispatchResult> PublishRawAsync(string topic, byte[] data) {
            return default(Task<DispatchResult>);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        var pubDiags = diags.Where(d => d.Id == "OUTBOX012").ToList();
        pubDiags.Should().HaveCount(2);
    }

    [Fact]
    public async Task AbandonedBuilder_NonBuilder_Discard_Should_Not_Report()
    {
        var source = @"
using System;
namespace Test {
    public class Usage {
        public int GetNumber() => 42;
        public void Run() {
            GetNumber();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX008").Should().BeEmpty();
    }

    [Fact]
    public async Task RetryPolicy_ObjectCreation_WithoutArgumentList_Should_Not_Crash()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class FixedDelayRetryPolicy {
        public int Attempts { get; set; }
    }
}
namespace Test {
    public class Usage {
        public void Configure() {
            var policy = new EricksonLopez.Outbox.FixedDelayRetryPolicy { Attempts = 3 };
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX004").Should().BeEmpty();
    }

    [Fact]
    public async Task RetryPolicy_ObjectCreation_WithExcessArguments_Should_Not_Crash()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class FixedDelayRetryPolicy {
        public FixedDelayRetryPolicy(int maxAttempts, int delayMs) { }
    }
}
namespace Test {
    public class Usage {
        public void Configure() {
            var policy = new EricksonLopez.Outbox.FixedDelayRetryPolicy(3, 100, 200, 300);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX004").Should().BeEmpty();
    }

    [Fact]
    public async Task AddOutbox_WithZeroArguments_Should_Not_Crash()
    {
        var source = @"
namespace Test {
    public class Setup {
        public void Configure() {
            AddOutbox();
        }
        public void AddOutbox() {}
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX005").Should().BeEmpty();
    }

    [Fact]
    public async Task AddOutbox_WithNonLambdaArgument_Should_Not_Crash()
    {
        var source = @"
using System;
namespace Test {
    public class Setup {
        public void Configure() {
            AddOutbox(null);
            Action<object> action = _ => {};
            AddOutbox(action);
        }
        public void AddOutbox(Action<object> opts) {}
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX005").Should().BeEmpty();
    }

    [Fact]
    public async Task AddOutbox_WithNonMemberAccessOrIdentifierInvocation_Should_Handle_Wildcard()
    {
        var source = @"
using System;
namespace Test {
    public class Setup {
        public void Configure() {
            AddOutbox(opts => {
                ((Action)(() => {}))();
            });
        }
        public void AddOutbox(Action<object> opts) {}
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().Contain(d => d.Id == "OUTBOX005");
    }

    [Fact]
    public async Task StoreAsync_WithNonNullTransaction_Should_Not_Report_OUTBOX007()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutboxTransactionContext {}
    public interface IOutbox {
        void StoreAsync<T>(T msg, IOutboxTransactionContext transaction);
    }
}
namespace Test {
    using EricksonLopez.Outbox;
    public class Usage {
        public void Run(IOutbox outbox, IOutboxTransactionContext tx) {
            outbox.StoreAsync(123, tx);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().NotContain(d => d.Id == "OUTBOX007");
    }

    [Fact]
    public async Task StoreAsync_WithParenthesizedNull_Should_Report_OUTBOX007()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutboxTransactionContext {}
    public interface IOutbox {
        void StoreAsync<T>(T msg, IOutboxTransactionContext transaction);
    }
}
namespace Test {
    using EricksonLopez.Outbox;
    public class Usage {
        public void Run(IOutbox outbox) {
            outbox.StoreAsync(123, ((IOutboxTransactionContext)null));
            outbox.StoreAsync(456, (null));
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Count(d => d.Id == "OUTBOX007").Should().Be(2);
    }

    [Fact]
    public async Task StoreAsync_WithExcessArguments_Should_Not_Crash()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutboxTransactionContext {}
    public interface IOutbox {
        void StoreAsync<T>(T msg, IOutboxTransactionContext transaction);
    }
}
namespace Test {
    using EricksonLopez.Outbox;
    public class Usage {
        public void Run(IOutbox outbox, IOutboxTransactionContext tx) {
            outbox.StoreAsync(123, tx, (object)null!, (object)null!, (object)null!);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX007").Should().BeEmpty();
    }

    [Fact]
    public async Task AbandonedBuilder_VoidExpressionStatement_Should_Not_Crash()
    {
        var source = @"
using System;
namespace Test {
    public class Usage {
        public void DoVoid() { }
        public void Run() {
            DoVoid();
            Console.WriteLine(10);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX008").Should().BeEmpty();
    }

    [Fact]
    public async Task MissingJsonSerializable_Should_Report_OUTBOX013()
    {
        var source = @"
namespace System.Text.Json.Serialization {
    public abstract class JsonSerializerContext {}
    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
    public class JsonSerializableAttribute : System.Attribute {
        public JsonSerializableAttribute(System.Type type) {}
    }
}
namespace EricksonLopez.Outbox.Contracts {
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class OutboxMessageAttribute : System.Attribute {}
}
namespace Test {
    [EricksonLopez.Outbox.Contracts.OutboxMessage]
    public class OrderCreatedEvent {
        public System.Guid Id { get; set; }
    }

    public class AppJsonContext : System.Text.Json.Serialization.JsonSerializerContext {
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().Contain(d => d.Id == "OUTBOX013");
    }

    [Fact]
    public async Task RegisteredJsonSerializable_Should_Not_Report_OUTBOX013()
    {
        var source = @"
namespace System.Text.Json.Serialization {
    public abstract class JsonSerializerContext {}
    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
    public class JsonSerializableAttribute : System.Attribute {
        public JsonSerializableAttribute(System.Type type) {}
    }
}
namespace EricksonLopez.Outbox.Contracts {
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class OutboxMessageAttribute : System.Attribute {}
}
namespace Test {
    [EricksonLopez.Outbox.Contracts.OutboxMessage]
    public class OrderCreatedEvent {
        public System.Guid Id { get; set; }
    }

    [System.Text.Json.Serialization.JsonSerializable(typeof(OrderCreatedEvent))]
    public class AppJsonContext : System.Text.Json.Serialization.JsonSerializerContext {
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Should().NotContain(d => d.Id == "OUTBOX013");
    }

    [Fact]
    public async Task JsonSerializerContext_WithMultipleNestedNamespaces_Should_Traverse_And_Handle_Visited_Namespaces()
    {
        var source = @"
namespace System.Text.Json.Serialization {
    public abstract class JsonSerializerContext {}
    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
    public class JsonSerializableAttribute : System.Attribute {
        public JsonSerializableAttribute(System.Type type) {}
    }
}
namespace EricksonLopez.Outbox.Contracts {
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class OutboxMessageAttribute : System.Attribute {}
}
namespace NestedA.Sub1 {
    [EricksonLopez.Outbox.Contracts.OutboxMessage]
    public class Event1 { public System.Guid Id { get; set; } }
}
namespace NestedA.Sub2 {
    [EricksonLopez.Outbox.Contracts.OutboxMessage]
    public class Event2 { public System.Guid Id { get; set; } }
}
namespace Test {
    [System.Text.Json.Serialization.JsonSerializable(typeof(NestedA.Sub1.Event1))]
    [System.Text.Json.Serialization.JsonSerializable(typeof(NestedA.Sub2.Event2))]
    public class AppJsonContext : System.Text.Json.Serialization.JsonSerializerContext {
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX013").Should().BeEmpty();
    }

    [Fact]
    public async Task StoreAsync_WithNamedNullMessageArgument_Should_Not_Report_OUTBOX007()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutboxTransactionContext {}
    public interface IOutbox {
        void StoreAsync<T>(T msg, IOutboxTransactionContext transaction);
    }
}
namespace Test {
    using EricksonLopez.Outbox;
    public class Usage {
        public void Run(IOutbox outbox, IOutboxTransactionContext tx) {
            outbox.StoreAsync(msg: (object)null, transaction: tx);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX007").Should().BeEmpty();
    }

    [Fact]
    public async Task RetryPolicy_WithParamsArgument_Should_Not_Crash()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public class FixedDelayRetryPolicy {
        public FixedDelayRetryPolicy(int maxAttempts, params int[] extra) { }
    }
}
namespace Test {
    public class Usage {
        public void Configure() {
            var policy = new EricksonLopez.Outbox.FixedDelayRetryPolicy(3, 10, 20, 30);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX004").Should().BeEmpty();
    }

    [Fact]
    public async Task StoreAsync_WithParamsArgument_Should_Not_Crash()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutboxTransactionContext {}
    public interface IOutbox {
        void StoreAsync<T>(T msg, IOutboxTransactionContext tx, params object[] extra);
    }
}
namespace Test {
    using EricksonLopez.Outbox;
    public class Usage {
        public void Run(IOutbox outbox, IOutboxTransactionContext tx) {
            outbox.StoreAsync(123, tx, (object)null, (object)null);
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX007").Should().BeEmpty();
    }

    [Fact]
    public async Task AbandonedBuilder_UnresolvedExpression_Should_Not_Crash()
    {
        var source = @"
namespace Test {
    public class Usage {
        public void Run() {
            NonExistentMethod();
            throw new System.Exception();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX008").Should().BeEmpty();
    }

    [Fact]
    public async Task AbandonedBuilder_CustomClass_ForeignInterface_Should_Not_Report_OUTBOX008()
    {
        var source = @"
namespace ForeignNamespace {
    public interface IOutboxMessageBuilder { }
}
namespace EricksonLopez.Outbox {
    public class CustomForeignBuilder : ForeignNamespace.IOutboxMessageBuilder { }
}
namespace Test {
    using EricksonLopez.Outbox;
    public class Usage {
        public CustomForeignBuilder GetBuilder() => new CustomForeignBuilder();
        public void Run() {
            GetBuilder();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX008").Should().BeEmpty();
    }

    [Fact]
    public async Task AbandonedBuilder_CustomClass_OtherFrameworkInterface_Should_Not_Report_OUTBOX008()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOtherInterface { }
    public class CustomOtherBuilder : IOtherInterface { }
}
namespace Test {
    using EricksonLopez.Outbox;
    public class Usage {
        public CustomOtherBuilder GetBuilder() => new CustomOtherBuilder();
        public void Run() {
            GetBuilder();
        }
    }
}";
        var diags = await GetDiagnosticsAsync(source);
        diags.Where(d => d.Id == "OUTBOX008").Should().BeEmpty();
    }


    private sealed class TestAnalysisContext : AnalysisContext
    {
        public bool ConcurrentExecutionEnabled { get; private set; }
        public bool GeneratedCodeAnalysisConfigured { get; private set; }
        public GeneratedCodeAnalysisFlags GeneratedCodeAnalysisFlags { get; private set; }
        public System.Collections.Generic.List<SyntaxKind> RegisteredSyntaxNodeActions { get; } = new();
        public System.Collections.Generic.List<SymbolKind> RegisteredSymbolKinds { get; } = new();
        public System.Collections.Generic.List<OperationKind> RegisteredOperationKinds { get; } = new();
        public int CompilationActionsCount { get; private set; }

        public override void EnableConcurrentExecution() => ConcurrentExecutionEnabled = true;

        public override void ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags analysisMode)
        {
            GeneratedCodeAnalysisConfigured = true;
            GeneratedCodeAnalysisFlags = analysisMode;
        }

        public override void RegisterSyntaxNodeAction<TLanguageKindEnum>(Action<SyntaxNodeAnalysisContext> action, ImmutableArray<TLanguageKindEnum> syntaxKinds)
        {
            foreach (var kind in syntaxKinds)
            {
                if (kind is SyntaxKind sk)
                    RegisteredSyntaxNodeActions.Add(sk);
            }
        }

        public override void RegisterSymbolAction(Action<SymbolAnalysisContext> action, ImmutableArray<SymbolKind> symbolKinds)
        {
            RegisteredSymbolKinds.AddRange(symbolKinds);
        }

        public override void RegisterOperationAction(Action<OperationAnalysisContext> action, ImmutableArray<OperationKind> operationKinds)
        {
            RegisteredOperationKinds.AddRange(operationKinds);
        }

        public override void RegisterCompilationAction(Action<CompilationAnalysisContext> action)
        {
            CompilationActionsCount++;
        }

        public override void RegisterCodeBlockAction(Action<CodeBlockAnalysisContext> action) { }
        public override void RegisterCodeBlockStartAction<TLanguageKindEnum>(Action<CodeBlockStartAnalysisContext<TLanguageKindEnum>> action) { }
        public override void RegisterCompilationStartAction(Action<CompilationStartAnalysisContext> action) { }
        public override void RegisterOperationBlockAction(Action<OperationBlockAnalysisContext> action) { }
        public override void RegisterOperationBlockStartAction(Action<OperationBlockStartAnalysisContext> action) { }
        public override void RegisterSemanticModelAction(Action<SemanticModelAnalysisContext> action) { }
        public override void RegisterSyntaxTreeAction(Action<SyntaxTreeAnalysisContext> action) { }
    }
}





