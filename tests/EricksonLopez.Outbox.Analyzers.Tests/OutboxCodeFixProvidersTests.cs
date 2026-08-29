// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Analyzers;

public class OutboxCodeFixProvidersTests
{
    private static async Task<(Document Document, Diagnostic Diagnostic)> GetDiagnosticAsync(string source, DiagnosticAnalyzer analyzer, string diagnosticId)
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("TestProj", LanguageNames.CSharp)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReference(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
            
        var document = project.AddDocument("Test.cs", source);
        
        var compilation = await document.Project.GetCompilationAsync();
        var compilationWithAnalyzers = compilation!.WithAnalyzers(ImmutableArray.Create(analyzer));
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        
        var diagnostic = diagnostics.FirstOrDefault(d => d.Id == diagnosticId);
        return (document, diagnostic!);
    }

    private static async Task<string> ApplyCodeFixAsync(Document document, Diagnostic diagnostic, CodeFixProvider provider)
    {
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic, (a, d) => actions.Add(a), CancellationToken.None);
        
        await provider.RegisterCodeFixesAsync(context);
        
        if (actions.Count == 0)
            return (await document.GetTextAsync()).ToString();
            
        var operations = await actions[0].GetOperationsAsync(CancellationToken.None);
        var applyChangesOperation = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
        
        if (applyChangesOperation == null)
            return (await document.GetTextAsync()).ToString();
            
        var newDoc = applyChangesOperation.ChangedSolution.GetDocument(document.Id);
        return (await newDoc!.GetTextAsync()).ToString();
    }

    [Fact]
    public void MissingIdCodeFixProvider_Should_Return_Properties()
    {
        var provider = new OutboxMissingIdCodeFixProvider();
        provider.FixableDiagnosticIds.Should().Equal("OUTBOX001");
        provider.GetFixAllProvider().Should().Be(WellKnownFixAllProviders.BatchFixer);
    }

    [Fact]
    public void MissingAliasCodeFixProvider_Should_Return_Properties()
    {
        var provider = new OutboxMissingAliasCodeFixProvider();
        provider.FixableDiagnosticIds.Should().Equal("OUTBOX002");
        provider.GetFixAllProvider().Should().Be(WellKnownFixAllProviders.BatchFixer);
    }

    [Fact]
    public void MissingInboxConsumerCodeFixProvider_Should_Return_Properties()
    {
        var provider = new OutboxMissingInboxConsumerCodeFixProvider();
        provider.FixableDiagnosticIds.Should().Equal("OUTBOX003");
        provider.GetFixAllProvider().Should().Be(WellKnownFixAllProviders.BatchFixer);
    }

    [Fact]
    public void InfiniteRetriesCodeFixProvider_Should_Return_Properties()
    {
        var provider = new OutboxInfiniteRetriesCodeFixProvider();
        provider.FixableDiagnosticIds.Should().Equal("OUTBOX004");
        provider.GetFixAllProvider().Should().Be(WellKnownFixAllProviders.BatchFixer);
    }

    [Fact]
    public void IntegrationEventAliasCodeFixProvider_Should_Return_Properties()
    {
        var provider = new OutboxIntegrationEventAliasCodeFixProvider();
        provider.FixableDiagnosticIds.Should().Equal("OUTBOX006");
        provider.GetFixAllProvider().Should().Be(WellKnownFixAllProviders.BatchFixer);
    }

    [Fact]
    public void MissingSerializerCodeFixProvider_Should_Return_Properties()
    {
        var provider = new OutboxMissingSerializerCodeFixProvider();
        provider.FixableDiagnosticIds.Should().Equal("OUTBOX005");
        provider.GetFixAllProvider().Should().Be(WellKnownFixAllProviders.BatchFixer);
    }

    [Fact]
    public void NullTransactionCodeFixProvider_Should_Return_Properties()
    {
        var provider = new OutboxNullTransactionCodeFixProvider();
        provider.FixableDiagnosticIds.Should().Equal("OUTBOX007");
        provider.GetFixAllProvider().Should().Be(WellKnownFixAllProviders.BatchFixer);
    }

    [Fact]
    public async Task MissingIdCodeFix_Should_Add_Guid_Id_Property()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutbox<T> { void StoreAsync<TMsg>(); }
}
namespace Test {
    public class MyMessage { }
    public class Usage {
        public void DoWork(EricksonLopez.Outbox.IOutbox<object> outbox) {
            outbox.StoreAsync<MyMessage>();
        }
    }
}";
        var (doc, diag) = await GetDiagnosticAsync(source, new OutboxMessageAnalyzer(), "OUTBOX001");
        diag.Should().NotBeNull("OUTBOX001 should be reported");

        var newCode = await ApplyCodeFixAsync(doc, diag, new OutboxMissingIdCodeFixProvider());
        
        newCode.Should().Contain("public Guid Id { get; } = Guid.NewGuid();");
    }

    [Fact]
    public async Task MissingAliasCodeFix_Should_Add_OutboxMessage_Attribute()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutbox<T> { void StoreAsync<TMsg>(); }
}
namespace Test {
    public class MyMessage { public Guid Id { get; set; } }
    public class Usage {
        public void DoWork(EricksonLopez.Outbox.IOutbox<object> outbox) {
            outbox.StoreAsync<MyMessage>();
        }
    }
}";
        var (doc, diag) = await GetDiagnosticAsync(source, new OutboxMessageAnalyzer(), "OUTBOX002");
        diag.Should().NotBeNull("OUTBOX002 should be reported");

        var newCode = await ApplyCodeFixAsync(doc, diag, new OutboxMissingAliasCodeFixProvider());
        
        newCode.Should().Contain("[EricksonLopez.Outbox.Contracts.OutboxMessage(\"usage\")]");
    }

    [Fact]
    public async Task MissingAliasCodeFix_With_PascalCase_Should_Convert_To_KebabCase()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutbox<T> { void StoreAsync<TMsg>(); }
}
namespace Test {
    public class OrderCreatedWorkflow {
        public void DoWork(EricksonLopez.Outbox.IOutbox<object> outbox) {
            outbox.StoreAsync<OrderCreatedWorkflow>();
        }
    }
}";
        var (doc, diag) = await GetDiagnosticAsync(source, new OutboxMessageAnalyzer(), "OUTBOX002");
        diag.Should().NotBeNull("OUTBOX002 should be reported");

        var newCode = await ApplyCodeFixAsync(doc, diag, new OutboxMissingAliasCodeFixProvider());
        
        newCode.Should().Contain("[EricksonLopez.Outbox.Contracts.OutboxMessage(\"order.created.workflow\")]");
    }

    [Fact]
    public async Task MissingInboxConsumerCodeFix_Should_Add_InboxConsumer_Attribute()
    {
        var source = @"
namespace Test {
    public interface IConsumer<T> { }
    public class MyMessageHandler : IConsumer<object> {
    }
}";
        var (doc, diag) = await GetDiagnosticAsync(source, new OutboxMessageAnalyzer(), "OUTBOX003");
        diag.Should().NotBeNull("OUTBOX003 should be reported");

        var newCode = await ApplyCodeFixAsync(doc, diag, new OutboxMissingInboxConsumerCodeFixProvider());
        
        newCode.Should().Contain("[EricksonLopez.Outbox.Contracts.InboxConsumer]");
    }

    [Fact]
    public async Task InfiniteRetriesCodeFix_Should_Set_MaxAttempts_To_3()
    {
        var source = @"
namespace Test {
    public class MyRetryPolicy { public MyRetryPolicy(int maxAttempts) {} }
    public class Usage {
        public void Create() {
            var policy = new MyRetryPolicy(maxAttempts: 0);
        }
    }
}";
        var (doc, diag) = await GetDiagnosticAsync(source, new OutboxMessageAnalyzer(), "OUTBOX004");
        diag.Should().NotBeNull("OUTBOX004 should be reported");

        var newCode = await ApplyCodeFixAsync(doc, diag, new OutboxInfiniteRetriesCodeFixProvider());
        
        newCode.Should().Contain("maxAttempts: 3");
    }

    [Fact]
    public async Task IntegrationEventAliasCodeFix_Should_Add_OutboxMessage_Attribute()
    {
        var source = @"
namespace EricksonLopez.Events {
    public interface IIntegrationEvent { }
}
namespace Test {
    public class MyEvent : EricksonLopez.Events.IIntegrationEvent { }
}";
        var (doc, diag) = await GetDiagnosticAsync(source, new OutboxMessageAnalyzer(), "OUTBOX006");
        diag.Should().NotBeNull("OUTBOX006 should be reported");

        var newCode = await ApplyCodeFixAsync(doc, diag, new OutboxIntegrationEventAliasCodeFixProvider());
        
        newCode.Should().Contain("[EricksonLopez.Outbox.Contracts.OutboxMessage(\"my.event\")]");
    }

    [Fact]
    public async Task MissingSerializerCodeFix_Should_Add_UseNativeAotJsonSerializer()
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
        var (doc, diag) = await GetDiagnosticAsync(source, new OutboxMessageAnalyzer(), "OUTBOX005");
        diag.Should().NotBeNull("OUTBOX005 should be reported");

        var newCode = await ApplyCodeFixAsync(doc, diag, new OutboxMissingSerializerCodeFixProvider());
        
        newCode.Should().Contain("opts.UseNativeAotJsonSerializer();");
    }

    [Fact]
    public async Task MissingIdCodeFix_EarlyExit_On_Missing_Root_Or_TypeDecl()
    {
        var provider = new OutboxMissingIdCodeFixProvider();
        // Since we can't easily mock syntax trees without root or typedecl, we just call it
        // and ensure it does not throw. The only way is to pass a diagnostic that points to nowhere.
        var workspace = new AdhocWorkspace();
        var proj = workspace.AddProject("TestProj", LanguageNames.CSharp);
        var doc = proj.AddDocument("Test.cs", "class C {}");
        var diag = Diagnostic.Create(OutboxMessageAnalyzer.MissingIdRule, Location.None);
        var ctx = new CodeFixContext(doc, diag, (a, d) => {}, CancellationToken.None);
        var act = () => provider.RegisterCodeFixesAsync(ctx);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task MissingAliasCodeFix_EarlyExit_On_Missing_Root_Or_TypeDecl()
    {
        var provider = new OutboxMissingAliasCodeFixProvider();
        var workspace = new AdhocWorkspace();
        var proj = workspace.AddProject("TestProj", LanguageNames.CSharp);
        var doc = proj.AddDocument("Test.cs", "class C {}");
        var diag = Diagnostic.Create(OutboxMessageAnalyzer.MissingAliasRule, Location.None);
        var ctx = new CodeFixContext(doc, diag, (a, d) => {}, CancellationToken.None);
        var act = () => provider.RegisterCodeFixesAsync(ctx);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NullTransactionCodeFix_Should_Replace_Null_With_Placeholder()
    {
        var source = @"
namespace EricksonLopez.Outbox {
    public interface IOutbox {
        Task StoreAsync<T>(T message, object transaction = null) where T : notnull;
    }
}
namespace Test {
    public class MyMessage { }
    public class Usage {
        public async Task DoWork(EricksonLopez.Outbox.IOutbox outbox) {
            await outbox.StoreAsync(new MyMessage(), null);
        }
    }
}";
        var (doc, diag) = await GetDiagnosticAsync(source, new OutboxMessageAnalyzer(), "OUTBOX007");
        diag.Should().NotBeNull("OUTBOX007 should be reported");

        var newCode = await ApplyCodeFixAsync(doc, diag, new OutboxNullTransactionCodeFixProvider());
        
        newCode.Should().Contain("transactionContext/* TODO: Provide IOutboxTransactionContext */");
    }

    [Fact]
    public async Task MissingSerializerCodeFix_Should_Support_ExpressionBody_Lambda()
    {
        var source = @"
namespace Test {
    public class Setup {
        public void Configure() {
            AddOutbox(opts => opts.ToString());
        }
        public void AddOutbox(System.Action<object> opts) {}
    }
}";
        var (doc, diag) = await GetDiagnosticAsync(source, new OutboxMessageAnalyzer(), "OUTBOX005");
        diag.Should().NotBeNull("OUTBOX005 should be reported");

        var newCode = await ApplyCodeFixAsync(doc, diag, new OutboxMissingSerializerCodeFixProvider());
        
        newCode.Should().Contain("opts.UseNativeAotJsonSerializer();");
    }

    [Fact]
    public async Task MissingSerializerCodeFix_Should_Support_Parenthesized_Lambda()
    {
        var source = @"
namespace Test {
    public class Setup {
        public void Configure() {
            AddOutbox((opts) => { });
        }
        public void AddOutbox(System.Action<object> opts) {}
    }
}";
        var (doc, diag) = await GetDiagnosticAsync(source, new OutboxMessageAnalyzer(), "OUTBOX005");
        diag.Should().NotBeNull("OUTBOX005 should be reported");

        var newCode = await ApplyCodeFixAsync(doc, diag, new OutboxMissingSerializerCodeFixProvider());
        
        newCode.Should().Contain("opts.UseNativeAotJsonSerializer();");
    }

    [Fact]
    public async Task MissingSerializerCodeFix_Should_Support_Parenthesized_ExpressionBody_Lambda()
    {
        var source = @"
namespace Test {
    public class Setup {
        public void Configure() {
            AddOutbox((opts) => opts.ToString());
        }
        public void AddOutbox(System.Action<object> opts) {}
    }
}";
        var (doc, diag) = await GetDiagnosticAsync(source, new OutboxMessageAnalyzer(), "OUTBOX005");
        diag.Should().NotBeNull("OUTBOX005 should be reported");

        var newCode = await ApplyCodeFixAsync(doc, diag, new OutboxMissingSerializerCodeFixProvider());
        
        newCode.Should().Contain("opts.UseNativeAotJsonSerializer();");
    }

    [Fact]
    public async Task MissingSerializerCodeFix_EarlyExit_On_Empty_ArgumentList()
    {
        var provider = new OutboxMissingSerializerCodeFixProvider();
        var workspace = new AdhocWorkspace();
        var proj = workspace.AddProject("TestProj", LanguageNames.CSharp);
        var doc = proj.AddDocument("Test.cs", "class C {}");
        var diag = Diagnostic.Create(OutboxMessageAnalyzer.SerializationConfigRule, Location.None);
        var ctx = new CodeFixContext(doc, diag, (a, d) => {}, CancellationToken.None);
        var act = () => provider.RegisterCodeFixesAsync(ctx);
        await act.Should().NotThrowAsync();
    }
    [Fact]
    public async Task MissingSerializerCodeFix_Should_Not_Crash_On_AnonymousMethod()
    {
        var source = @"
namespace Test {
    public class Setup {
        public void Configure() {
            AddOutbox(delegate (object opts) { });
        }
        public void AddOutbox(System.Action<object> opts) {}
    }
}";
        var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
        var project = workspace.AddProject("TestProj", Microsoft.CodeAnalysis.LanguageNames.CSharp);
        var doc = project.AddDocument("Test.cs", source);
        var tree = await doc.GetSyntaxTreeAsync();
        var root = await tree!.GetRootAsync();
        
        var invocation = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .First(n => n.Expression.ToString() == "AddOutbox");
            
        var diag = Microsoft.CodeAnalysis.Diagnostic.Create(
            new Microsoft.CodeAnalysis.DiagnosticDescriptor("OUTBOX005", "Title", "Message", "Category", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, true),
            invocation.GetLocation());

        var newCode = await ApplyCodeFixAsync(doc, diag, new OutboxMissingSerializerCodeFixProvider());
        
        // Unchanged since it doesn't support anonymous methods
        newCode.Should().Contain("delegate (object opts)");
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("a", "a")]
    [InlineData("A", "a")]
    [InlineData("Order", "order")]
    [InlineData("OrderCreated", "order.created")]
    [InlineData("OrderCreatedEvent", "order.created.event")]
    [InlineData("ABC", "a.b.c")]
    [InlineData("Order123Created", "order123.created")]
    public void ToKebabCase_Should_Convert_Correctly(string input, string expected)
    {
        OutboxMissingAliasCodeFixProvider.ToKebabCase(input).Should().Be(expected);
        OutboxIntegrationEventAliasCodeFixProvider.ToKebabCase(input).Should().Be(expected);
    }

    [Fact]
    public async Task MissingSerializerCodeFix_Should_Support_Parameterless_Parenthesized_Lambda_Block()
    {
        var source = @"
namespace Test {
    public class Setup {
        public void Configure() {
            AddOutbox(() => { });
        }
        public void AddOutbox(System.Action configure) {}
    }
}";
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("TestProj", LanguageNames.CSharp);
        var doc = project.AddDocument("Test.cs", source);
        var tree = await doc.GetSyntaxTreeAsync();
        var root = await tree!.GetRootAsync();
        var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().First(n => n.Expression.ToString() == "AddOutbox");
        var diag = Diagnostic.Create(OutboxMessageAnalyzer.SerializationConfigRule, invocation.GetLocation());

        var newCode = await ApplyCodeFixAsync(doc, diag, new OutboxMissingSerializerCodeFixProvider());
        newCode.Should().Contain("opts.UseNativeAotJsonSerializer();");
    }

    [Fact]
    public async Task MissingSerializerCodeFix_Should_Support_Parameterless_Parenthesized_Lambda_ExpressionBody()
    {
        var source = @"
namespace Test {
    public class Setup {
        public void Configure() {
            AddOutbox(() => 123);
        }
        public void AddOutbox(System.Func<int> configure) {}
    }
}";
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("TestProj", LanguageNames.CSharp);
        var doc = project.AddDocument("Test.cs", source);
        var tree = await doc.GetSyntaxTreeAsync();
        var root = await tree!.GetRootAsync();
        var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().First(n => n.Expression.ToString() == "AddOutbox");
        var diag = Diagnostic.Create(OutboxMessageAnalyzer.SerializationConfigRule, invocation.GetLocation());

        var newCode = await ApplyCodeFixAsync(doc, diag, new OutboxMissingSerializerCodeFixProvider());
        newCode.Should().Contain("opts.UseNativeAotJsonSerializer();");
    }

    [Fact]
    public async Task MissingSerializerCodeFix_Should_Support_Custom_Parameter_Name()
    {
        var source = @"
namespace Test {
    public class Setup {
        public void Configure() {
            AddOutbox((customConfig) => { });
        }
        public void AddOutbox(System.Action<object> configure) {}
    }
}";
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("TestProj", LanguageNames.CSharp);
        var doc = project.AddDocument("Test.cs", source);
        var tree = await doc.GetSyntaxTreeAsync();
        var root = await tree!.GetRootAsync();
        var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().First(n => n.Expression.ToString() == "AddOutbox");
        var diag = Diagnostic.Create(OutboxMessageAnalyzer.SerializationConfigRule, invocation.GetLocation());

        var newCode = await ApplyCodeFixAsync(doc, diag, new OutboxMissingSerializerCodeFixProvider());
        newCode.Should().Contain("customConfig.UseNativeAotJsonSerializer();");
    }

    [Fact]
    public async Task All_CodeFixProviders_Should_Gracefully_Handle_Missing_Nodes()
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("TestProj", LanguageNames.CSharp);
        var doc = project.AddDocument("Test.cs", "int x = 0;");
        var tree = await doc.GetSyntaxTreeAsync();
        var root = await tree!.GetRootAsync();
        var statement = root.DescendantNodes().OfType<GlobalStatementSyntax>().First();
        var diag = Diagnostic.Create(OutboxMessageAnalyzer.MissingIdRule, statement.GetLocation());
        int registeredCount = 0;
        var context = new CodeFixContext(doc, diag, (a, d) => { registeredCount++; }, CancellationToken.None);

        await new OutboxMissingIdCodeFixProvider().RegisterCodeFixesAsync(context);
        await new OutboxMissingAliasCodeFixProvider().RegisterCodeFixesAsync(context);
        await new OutboxNullTransactionCodeFixProvider().RegisterCodeFixesAsync(context);
        await new OutboxMissingInboxConsumerCodeFixProvider().RegisterCodeFixesAsync(context);
        await new OutboxInfiniteRetriesCodeFixProvider().RegisterCodeFixesAsync(context);
        await new OutboxIntegrationEventAliasCodeFixProvider().RegisterCodeFixesAsync(context);
        await new OutboxMissingSerializerCodeFixProvider().RegisterCodeFixesAsync(context);

        registeredCount.Should().Be(0);
    }
}





