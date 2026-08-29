// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox.Inbox;
using EricksonLopez.Outbox.Inbox.AspNetCore;
using EricksonLopez.Outbox.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Inbox.AspNetCore.Tests;

public class IdempotentEndpointFilterTests
{
    [Fact]
    public void Constructor_DefaultHeader_SetsDefaultHeader()
    {
        var filter = new IdempotentEndpointFilter();
        filter.Should().NotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidHeader_FallsBackToDefault(string? invalidHeader)
    {
        var filter = new IdempotentEndpointFilter(invalidHeader!);
        filter.Should().NotBeNull();
    }

    [Fact]
    public async Task InvokeAsync_MissingIdempotencyHeader_ExecutesNextDirectly()
    {
        var filter = new IdempotentEndpointFilter();
        var context = CreateContext();
        var executed = false;

        EndpointFilterDelegate next = _ =>
        {
            executed = true;
            return ValueTask.FromResult<object?>("Success");
        };

        var result = await filter.InvokeAsync(context, next);

        executed.Should().BeTrue();
        result.Should().Be("Success");
    }

    [Fact]
    public async Task InvokeAsync_WhitespaceIdempotencyHeader_ExecutesNextDirectly()
    {
        var filter = new IdempotentEndpointFilter();
        var context = CreateContext();
        context.HttpContext.Request.Headers["Idempotency-Key"] = "   ";
        var executed = false;

        EndpointFilterDelegate next = _ =>
        {
            executed = true;
            return ValueTask.FromResult<object?>("Success");
        };

        var result = await filter.InvokeAsync(context, next);

        executed.Should().BeTrue();
        result.Should().Be("Success");
    }

    [Fact]
    public async Task InvokeAsync_MissingInboxFilterService_ExecutesNextDirectly()
    {
        var filter = new IdempotentEndpointFilter();
        var services = new ServiceCollection().BuildServiceProvider();
        var context = CreateContext(services);
        context.HttpContext.Request.Headers["Idempotency-Key"] = "test-key-123";
        var executed = false;

        EndpointFilterDelegate next = _ =>
        {
            executed = true;
            return ValueTask.FromResult<object?>("Success");
        };

        var result = await filter.InvokeAsync(context, next);

        executed.Should().BeTrue();
        result.Should().Be("Success");
    }

    [Fact]
    public async Task InvokeAsync_ValidIdempotencyHeader_ExecutesThroughInboxFilter()
    {
        var filter = new IdempotentEndpointFilter();
        var inboxFilter = Substitute.For<IInboxConsumerFilter>();
        inboxFilter.ExecuteIdempotentlyAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            Arg.Any<IOutboxTransactionContext?>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var handler = callInfo.Arg<Func<CancellationToken, ValueTask>>();
                return InvokeHandlerAsync(handler);
            });

        var services = new ServiceCollection()
            .AddSingleton(inboxFilter)
            .BuildServiceProvider();

        var context = CreateContext(services);
        context.HttpContext.Request.Headers["Idempotency-Key"] = "msg-uuid-999";
        context.HttpContext.Request.Path = "/api/orders/create";

        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>("OrderCreated");

        var result = await filter.InvokeAsync(context, next);

        result.Should().Be("OrderCreated");
        _ = inboxFilter.Received(1).ExecuteIdempotentlyAsync(
            "msg-uuid-999",
            "/api/orders/create",
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            null,
            context.HttpContext.RequestAborted);
    }

    [Fact]
    public async Task InvokeAsync_NullPathValue_UsesDefaultHttpEndpointConsumerName()
    {
        var filter = new IdempotentEndpointFilter();
        var inboxFilter = Substitute.For<IInboxConsumerFilter>();
        inboxFilter.ExecuteIdempotentlyAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            Arg.Any<IOutboxTransactionContext?>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var handler = callInfo.Arg<Func<CancellationToken, ValueTask>>();
                return InvokeHandlerAsync(handler);
            });

        var services = new ServiceCollection()
            .AddSingleton(inboxFilter)
            .BuildServiceProvider();

        var context = CreateContext(services);
        context.HttpContext.Request.Headers["Idempotency-Key"] = "msg-uuid-default-endpoint";
        context.HttpContext.Request.Path = new PathString(null);

        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>("DefaultEndpointHandled");

        var result = await filter.InvokeAsync(context, next);

        result.Should().Be("DefaultEndpointHandled");
        _ = inboxFilter.Received(1).ExecuteIdempotentlyAsync(
            "msg-uuid-default-endpoint",
            "HttpEndpoint",
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            null,
            context.HttpContext.RequestAborted);
    }

    private static async ValueTask<bool> InvokeHandlerAsync(Func<CancellationToken, ValueTask> handler)
    {
        await handler(CancellationToken.None).ConfigureAwait(false);
        return true;
    }

    [Fact]
    public async Task InvokeAsync_DuplicateRequestAlreadyProcessed_ReturnsConflictResultWithDetails()
    {
        var filter = new IdempotentEndpointFilter();
        var inboxFilter = Substitute.For<IInboxConsumerFilter>();
        inboxFilter.ExecuteIdempotentlyAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            Arg.Any<IOutboxTransactionContext?>(),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));

        var services = new ServiceCollection()
            .AddSingleton(inboxFilter)
            .BuildServiceProvider();

        var context = CreateContext(services);
        context.HttpContext.Request.Headers["Idempotency-Key"] = "dup-key-123";

        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>("ShouldNotBeCalled");

        var result = await filter.InvokeAsync(context, next);

        result.Should().NotBeNull();
        var statusCodeResult = result.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject;
        statusCodeResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);

        var valueResult = result.Should().BeAssignableTo<IValueHttpResult>().Subject;
        var value = valueResult.Value;
        value.Should().NotBeNull();

        var propMessage = value!.GetType().GetProperty("Message")?.GetValue(value) as string;
        propMessage.Should().Be("A request with Idempotency-Key 'dup-key-123' was already processed or is currently in-flight.");

        var propError = value.GetType().GetProperty("Error")?.GetValue(value) as string;
        propError.Should().Be("IdempotentRequestAlreadyProcessed");
    }

    [Fact]
    public async Task Constructor_CustomHeader_SetsAndReadsCustomHeader()
    {
        var filter = new IdempotentEndpointFilter("X-Custom-Idempotency");
        var inboxFilter = Substitute.For<IInboxConsumerFilter>();
        inboxFilter.ExecuteIdempotentlyAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            Arg.Any<IOutboxTransactionContext?>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var handler = callInfo.Arg<Func<CancellationToken, ValueTask>>();
                return InvokeHandlerAsync(handler);
            });

        var services = new ServiceCollection().AddSingleton(inboxFilter).BuildServiceProvider();
        var context = CreateContext(services);
        context.HttpContext.Request.Headers["X-Custom-Idempotency"] = "custom-key-777";

        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>("CustomSuccess");
        var result = await filter.InvokeAsync(context, next);

        result.Should().Be("CustomSuccess");
        _ = inboxFilter.Received(1).ExecuteIdempotentlyAsync(
            "custom-key-777",
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            null,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Constructor_NullOrWhitespaceHeader_FallsBackToDefaultHeader(string? invalidHeader)
    {
        var filter = new IdempotentEndpointFilter(invalidHeader!);
        var inboxFilter = Substitute.For<IInboxConsumerFilter>();
        inboxFilter.ExecuteIdempotentlyAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            Arg.Any<IOutboxTransactionContext?>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var handler = callInfo.Arg<Func<CancellationToken, ValueTask>>();
                return InvokeHandlerAsync(handler);
            });

        var services = new ServiceCollection().AddSingleton(inboxFilter).BuildServiceProvider();
        var context = CreateContext(services);
        context.HttpContext.Request.Headers["Idempotency-Key"] = "default-key-888";

        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>("DefaultSuccess");
        var result = await filter.InvokeAsync(context, next);

        result.Should().Be("DefaultSuccess");
        _ = inboxFilter.Received(1).ExecuteIdempotentlyAsync(
            "default-key-888",
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            null,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "")]
    [InlineData(true, "   ")]
    public async Task InvokeAsync_MissingOrWhitespaceHeader_WithInboxFilterRegistered_DoesNotCallInboxFilter(bool setHeader, string? headerValue)
    {
        var filter = new IdempotentEndpointFilter();
        var inboxFilter = Substitute.For<IInboxConsumerFilter>();
        var services = new ServiceCollection().AddSingleton(inboxFilter).BuildServiceProvider();
        var context = CreateContext(services);
        if (setHeader)
        {
            context.HttpContext.Request.Headers["Idempotency-Key"] = headerValue;
        }

        var executed = false;
        EndpointFilterDelegate next = _ =>
        {
            executed = true;
            return ValueTask.FromResult<object?>("DirectSuccess");
        };

        var result = await filter.InvokeAsync(context, next);

        executed.Should().BeTrue();
        result.Should().Be("DirectSuccess");
        _ = inboxFilter.DidNotReceiveWithAnyArgs().ExecuteIdempotentlyAsync(
            default!,
            default!,
            default!,
            default,
            default);
    }

    [Fact]
    public async Task InvokeAsync_WhenHandledIsTrueAndResultIsNull_ReturnsNullWithoutConflict()
    {
        var filter = new IdempotentEndpointFilter();
        var inboxFilter = Substitute.For<IInboxConsumerFilter>();
        inboxFilter.ExecuteIdempotentlyAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            Arg.Any<IOutboxTransactionContext?>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var handler = callInfo.Arg<Func<CancellationToken, ValueTask>>();
                return InvokeHandlerAsync(handler);
            });

        var services = new ServiceCollection().AddSingleton(inboxFilter).BuildServiceProvider();
        var context = CreateContext(services);
        context.HttpContext.Request.Headers["Idempotency-Key"] = "no-content-key";

        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(null);

        var result = await filter.InvokeAsync(context, next);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RequireIdempotency_RouteHandlerBuilder_AddsFilterAndExecutesInPipeline()
    {
        var app = WebApplication.Create();
        var inboxFilter = Substitute.For<IInboxConsumerFilter>();
        inboxFilter.ExecuteIdempotentlyAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            Arg.Any<IOutboxTransactionContext?>(),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));

        var routeBuilder = app.MapGet("/test", () => "ok");
        var result = routeBuilder.RequireIdempotency("X-Custom-Key");
        result.Should().BeSameAs(routeBuilder);

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .Single();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().AddSingleton(inboxFilter).BuildServiceProvider()
        };
        httpContext.Request.Headers["X-Custom-Key"] = "k1";
        httpContext.Request.Path = "/test";

        await endpoint.RequestDelegate!(httpContext);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task RequireIdempotency_RouteGroupBuilder_AddsFilterAndExecutesInPipeline()
    {
        var app = WebApplication.Create();
        var inboxFilter = Substitute.For<IInboxConsumerFilter>();
        inboxFilter.ExecuteIdempotentlyAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            Arg.Any<IOutboxTransactionContext?>(),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));

        var group = app.MapGroup("/group");
        var result = group.RequireIdempotency("X-Group-Key");
        result.Should().BeSameAs(group);
        group.MapGet("/sub", () => "ok");

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .Single();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().AddSingleton(inboxFilter).BuildServiceProvider()
        };
        httpContext.Request.Headers["X-Group-Key"] = "g1";
        httpContext.Request.Path = "/group/sub";

        await endpoint.RequestDelegate!(httpContext);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task RequireIdempotency_RouteHandlerBuilder_WithDefaultHeaderName_AddsFilterAndExecutesInPipeline()
    {
        var app = WebApplication.Create();
        var inboxFilter = Substitute.For<IInboxConsumerFilter>();
        inboxFilter.ExecuteIdempotentlyAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            Arg.Any<IOutboxTransactionContext?>(),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));

        var routeBuilder = app.MapGet("/test-default", () => "ok");
        var result = routeBuilder.RequireIdempotency();
        result.Should().BeSameAs(routeBuilder);

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .Single();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().AddSingleton(inboxFilter).BuildServiceProvider()
        };
        httpContext.Request.Headers["Idempotency-Key"] = "default-k1";
        httpContext.Request.Path = "/test-default";

        await endpoint.RequestDelegate!(httpContext);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task RequireIdempotency_RouteGroupBuilder_WithDefaultHeaderName_AddsFilterAndExecutesInPipeline()
    {
        var app = WebApplication.Create();
        var inboxFilter = Substitute.For<IInboxConsumerFilter>();
        inboxFilter.ExecuteIdempotentlyAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            Arg.Any<IOutboxTransactionContext?>(),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));

        var group = app.MapGroup("/group-default");
        var result = group.RequireIdempotency();
        result.Should().BeSameAs(group);
        group.MapGet("/sub", () => "ok");

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .Single();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().AddSingleton(inboxFilter).BuildServiceProvider()
        };
        httpContext.Request.Headers["Idempotency-Key"] = "default-g1";
        httpContext.Request.Path = "/group-default/sub";

        await endpoint.RequestDelegate!(httpContext);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void RequireIdempotency_NullBuilder_ThrowsArgumentNullException()
    {
        RouteHandlerBuilder builder = null!;
        Action act = () => builder.RequireIdempotency();
        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    [Fact]
    public void RequireIdempotency_RouteGroup_NullBuilder_ThrowsArgumentNullException()
    {
        RouteGroupBuilder builder = null!;
        Action act = () => builder.RequireIdempotency();
        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    private static TestEndpointFilterInvocationContext CreateContext(IServiceProvider? serviceProvider = null)
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider ?? new ServiceCollection().BuildServiceProvider()
        };

        return new TestEndpointFilterInvocationContext(httpContext);
    }

    private sealed class TestEndpointFilterInvocationContext : EndpointFilterInvocationContext
    {
        public TestEndpointFilterInvocationContext(HttpContext httpContext)
        {
            HttpContext = httpContext;
        }

        public override HttpContext HttpContext { get; }
        public override IList<object?> Arguments { get; } = new List<object?>();

        public override T GetArgument<T>(int index) => (T)Arguments[index]!;
    }
}
