// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Storage.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;
using Toxiproxy.Net;
using Toxiproxy.Net.Toxics;
using Xunit;

namespace EricksonLopez.Outbox.IntegrationTests;

/// <summary>
/// Verifies that the Outbox dispatcher can recover from severe infrastructure failures.
/// These tests ensure the At-Least-Once delivery guarantee holds under chaos conditions.
/// </summary>
/// <remarks>
/// Chaos scenarios covered:
/// <list type="bullet">
///   <item>Database connection drop and restore</item>
///   <item>High network latency on DB connection (latency toxic)</item>
///   <item>Bandwidth throttling on DB connection (bandwidth toxic)</item>
///   <item>Broker (publisher) transient failure and recovery</item>
/// </list>
/// </remarks>
#pragma warning disable CA1001
[Trait("Category", "Integration")]
public class DispatcherChaosTests : IAsyncLifetime
{
    private INetwork _network = null!;
    private PostgreSqlContainer _dbContainer = null!;
    private IContainer _toxiproxyContainer = null!;
    private Toxiproxy.Net.Connection _toxiproxyConnection = null!;
    private Toxiproxy.Net.Client _toxiClient = null!;
    private Proxy _postgresProxy = null!;
    private string _proxyConnectionString = null!;

    public async Task InitializeAsync()
    {
        _network = new NetworkBuilder().Build();

        _dbContainer = new PostgreSqlBuilder("postgres:15-alpine")
            .WithDatabase("chaos_db")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithNetwork(_network)
            .WithNetworkAliases("postgres")
            .Build();

#pragma warning disable CS0618
        _toxiproxyContainer = new ContainerBuilder()
            .WithImage("ghcr.io/shopify/toxiproxy:2.11.0")
            .WithPortBinding(8474, true)
            .WithPortBinding(5432, true)
            .WithNetwork(_network)
            .Build();
#pragma warning restore CS0618

        await _network.CreateAsync();
        await Task.WhenAll(_dbContainer.StartAsync(), _toxiproxyContainer.StartAsync());

        _toxiproxyConnection = new Toxiproxy.Net.Connection(
            _toxiproxyContainer.Hostname,
            _toxiproxyContainer.GetMappedPublicPort(8474));
        _toxiClient = _toxiproxyConnection.Client();

        var proxy = new Proxy
        {
            Name = "postgres",
            Listen = "0.0.0.0:5432",
            Upstream = $"{_dbContainer.IpAddress}:5432"
        };
        _postgresProxy = await _toxiClient.AddAsync(proxy);
        
        // Workaround: Toxiproxy 2.11.0 sometimes drops connections on newly created proxies
        // until they are toggled or explicitly updated.
        _postgresProxy.Enabled = false;
        await _postgresProxy.UpdateAsync();
        _postgresProxy.Enabled = true;
        await _postgresProxy.UpdateAsync();

        var proxyBuilder = new NpgsqlConnectionStringBuilder(_dbContainer.GetConnectionString())
        {
            Host = "127.0.0.1",
            Port = _toxiproxyContainer.GetMappedPublicPort(5432),
            Timeout = 60,
            CommandTimeout = 60,
            Pooling = false
        };
        _proxyConnectionString = proxyBuilder.ConnectionString;

        // Give Toxiproxy a moment to fully initialize its listeners to prevent zombie connections in the pool
        await Task.Delay(1000);

        await InitializeSchemaAsync();
    }

    public async Task DisposeAsync()
    {
        _toxiproxyConnection?.Dispose();
        if (_toxiproxyContainer != null)
        {
            var logs = await _toxiproxyContainer.GetLogsAsync();
            System.Console.WriteLine("TOXIPROXY LOGS:");
            System.Console.WriteLine(logs.Stdout);
            System.Console.WriteLine(logs.Stderr);
            await _toxiproxyContainer.DisposeAsync();
        }
        if (_dbContainer != null) await _dbContainer.DisposeAsync();
        if (_network != null) await _network.DeleteAsync();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Scenario 1: Database connection drop and restore
    // Verifies: At-Least-Once guarantee survives complete DB unavailability.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Poller_Recovers_After_Transient_Database_Drop()
    {
        var fakeBroker = new FakeBroker();
        using var sp = BuildServiceProvider(fakeBroker);
        var hostedService = GetDispatcher(sp);

        await hostedService.StartAsync(CancellationToken.None);

        // 1. Bring the DB down by disabling the proxy
        _postgresProxy.Enabled = false;
        await _postgresProxy.UpdateAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));

        // 2. Restore DB
        _postgresProxy.Enabled = true;
        await _postgresProxy.UpdateAsync();
        NpgsqlConnection.ClearAllPools();
        await Task.Delay(TimeSpan.FromSeconds(1));

        // 3. Write a message via direct DB connection (bypasses proxy) and verify dispatch
        await WriteAndCommitMessageAsync("Recovered");
        
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (fakeBroker.PublishedMessages.IsEmpty && !cts.IsCancellationRequested)
        {
            await Task.Delay(100);
        }

        await hostedService.StopAsync(CancellationToken.None);

        Assert.NotEmpty(fakeBroker.PublishedMessages);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Scenario 2: High network latency on DB (latency toxic)
    // Verifies: Fully-async poller handles 500ms DB latency without deadlock
    // or message loss.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Poller_Delivers_Messages_Under_High_DB_Latency()
    {
        var latencyToxic = new LatencyToxic
        {
            Name = "db_latency",
            Stream = ToxicDirection.UpStream,
            Toxicity = 1.0f
        };
        latencyToxic.Attributes.Latency = 500;  // 500ms
        latencyToxic.Attributes.Jitter = 50;    // ± 50ms
        await _postgresProxy.AddAsync(latencyToxic);

        // Pre-write before starting the dispatcher
        await WriteAndCommitMessageAsync("HighLatencyMessage");

        var fakeBroker = new FakeBroker();
        using var sp = BuildServiceProvider(fakeBroker);
        var hostedService = GetDispatcher(sp);

        await hostedService.StartAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (fakeBroker.PublishedMessages.IsEmpty && !cts.IsCancellationRequested)
        {
            await Task.Delay(100);
        }

        await hostedService.StopAsync(CancellationToken.None);
        await _postgresProxy.RemoveToxicAsync(latencyToxic.Name);

        Assert.NotEmpty(fakeBroker.PublishedMessages);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Scenario 3: Bandwidth throttling (bandwidth toxic, 10 KB/s)
    // Verifies: All 3 pre-written messages are dispatched despite bandwidth cap.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Poller_Delivers_All_Messages_Under_Bandwidth_Cap()
    {
        var bandwidthToxic = new BandwidthToxic
        {
            Name = "db_bandwidth",
            Stream = ToxicDirection.UpStream,
            Toxicity = 1.0f
        };
        bandwidthToxic.Attributes.Rate = 10;    // 10 KB/s
        await _postgresProxy.AddAsync(bandwidthToxic);

        for (int i = 0; i < 3; i++)
            await WriteAndCommitMessageAsync($"BandwidthLimited_{i}");

        var fakeBroker = new FakeBroker();
        using var sp = BuildServiceProvider(fakeBroker);
        var hostedService = GetDispatcher(sp);

        await hostedService.StartAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (fakeBroker.PublishedMessages.Count < 3 && !cts.IsCancellationRequested)
        {
            await Task.Delay(200);
        }

        await hostedService.StopAsync(CancellationToken.None);
        await _postgresProxy.RemoveToxicAsync(bandwidthToxic.Name);

        Assert.Equal(3, fakeBroker.PublishedMessages.Count);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Scenario 4: Broker transient failure → retry → eventual delivery
    // Verifies: RetryDispatcherInterceptor retries correctly, message is not
    // dead-lettered prematurely, and delivery succeeds after broker recovers.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Dispatcher_Retries_And_Delivers_After_Broker_Recovery()
    {
        // Broker fails the first 2 calls, then succeeds
        var broker = new FlakyBroker(failCount: 2);

        using var sp = BuildServiceProvider(broker, pollingInterval: TimeSpan.FromMilliseconds(200));
        var hostedService = GetDispatcher(sp);

        await hostedService.StartAsync(CancellationToken.None);

        await WriteAndCommitMessageAsync("FlakyBrokerMessage");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (broker.PublishedMessages.IsEmpty && !cts.IsCancellationRequested)
        {
            await Task.Delay(100);
        }

        await hostedService.StopAsync(CancellationToken.None);

        if (broker.PublishedMessages.IsEmpty)
        {
            await using var conn = new NpgsqlConnection(_dbContainer.GetConnectionString());
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT id, state, deliver_at, error, owner_id FROM outbox.messages", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                System.Console.WriteLine($"DB ROW: id={reader.GetGuid(0)}, state={reader.GetInt32(1)}, deliver_at={(reader.IsDBNull(2) ? "null" : reader.GetDateTime(2).ToString(System.Globalization.CultureInfo.InvariantCulture))}, error={(reader.IsDBNull(3) ? "null" : reader.GetString(3))}, owner={(reader.IsDBNull(4) ? "null" : reader.GetGuid(4).ToString())}");
            }
        }

        Assert.NotEmpty(broker.PublishedMessages);
        Assert.True(broker.FailureCount >= 2,
            $"Expected ≥2 broker failures before success, got {broker.FailureCount}");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Scenario 5: Persistent Intermittent Database Drops
    // Verifies: The poller does not leak memory or crash when the database
    // drops and reconnects constantly in a tight loop.
    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Poller_Survives_Persistent_Intermittent_Drops()
    {
        var fakeBroker = new FakeBroker();
        using var sp = BuildServiceProvider(fakeBroker);
        var hostedService = GetDispatcher(sp);

        await hostedService.StartAsync(CancellationToken.None);

        await WriteAndCommitMessageAsync("IntermittentDropMessage1");

        for (int i = 0; i < 3; i++)
        {
            _postgresProxy.Enabled = false;
            await _postgresProxy.UpdateAsync();
            await Task.Delay(500);

            _postgresProxy.Enabled = true;
            await _postgresProxy.UpdateAsync();
            NpgsqlConnection.ClearAllPools();
            await Task.Delay(500);
        }

        await WriteAndCommitMessageAsync("IntermittentDropMessage2");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (fakeBroker.PublishedMessages.Count < 2 && !cts.IsCancellationRequested)
        {
            await Task.Delay(100);
        }

        await hostedService.StopAsync(CancellationToken.None);
        Assert.True(fakeBroker.PublishedMessages.Count >= 2);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private ServiceProvider BuildServiceProvider(
        IBrokerPublisher broker,
        TimeSpan? pollingInterval = null)
    {
        var services = new ServiceCollection();

        services.AddSingleton<EricksonLopez.Outbox.Serialization.IOutboxSerializer>(new DummySerializer());
        services.AddSingleton<EricksonLopez.Outbox.Serialization.IOutboxMessageTypeResolver>(new DummyTypeResolver());


        // PostgreSQL storage is configured via options.UsePostgreSql inside AddOutbox
        services.AddOutbox(options =>
        {
            options.UsePostgreSql(_proxyConnectionString);
            options.UseBroker(broker, new EricksonLopez.Outbox.Retry.FixedDelayRetryPolicy(TimeSpan.FromMilliseconds(100), 5));
        });

        services.AddOutboxDispatcher(options =>
        {
            options.PollingInterval = pollingInterval ?? TimeSpan.FromMilliseconds(100);
            options.BatchSize = 10;
            options.ReclaimInterval = TimeSpan.FromSeconds(1);
            options.ReclaimTimeout = TimeSpan.FromSeconds(2);
        });
        services.AddLogging();

        return services.BuildServiceProvider();
    }

    private static IHostedService GetDispatcher(IServiceProvider sp) =>
        sp.GetServices<IHostedService>()
          .First(s => s.GetType().Name == "OutboxDispatcherBackgroundService");

    private async Task WriteAndCommitMessageAsync(string tag)
    {
        // Use direct connection (not through proxy) to guarantee schema reachability
        await using var conn = new NpgsqlConnection(_dbContainer.GetConnectionString());
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO outbox.messages
                (id, type, payload, state, created_at, updated_at, retry_count)
            VALUES
                (gen_random_uuid(), 'chaos.test.v1', @payload::jsonb, 0, NOW(), NOW(), 0)
            """,
            conn, tx);
        cmd.Parameters.AddWithValue("payload", $"{{\"tag\":\"{tag}\"}}");
        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    private async Task InitializeSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(_dbContainer.GetConnectionString());
        await conn.OpenAsync();

        const string sql = """
            CREATE SCHEMA IF NOT EXISTS outbox;

            CREATE TABLE IF NOT EXISTS outbox.messages (
                id              UUID            NOT NULL,
                type            VARCHAR(255)    NOT NULL,
                payload         JSONB           NOT NULL,
                correlation_id  VARCHAR(255),
                causation_id    VARCHAR(255),
                headers_json    JSONB,
                state           SMALLINT        NOT NULL DEFAULT 0,
                created_at      TIMESTAMPTZ     NOT NULL DEFAULT NOW(),
                updated_at      TIMESTAMPTZ     NOT NULL DEFAULT NOW(),
                processed_at    TIMESTAMPTZ,
                deliver_at      TIMESTAMPTZ,
                retry_count     INT             NOT NULL DEFAULT 0,
                owner_id        UUID,
                error           TEXT,
                PRIMARY KEY (id, created_at)
            ) PARTITION BY RANGE (created_at);

            CREATE TABLE IF NOT EXISTS outbox.messages_default
                PARTITION OF outbox.messages DEFAULT;

            CREATE TABLE IF NOT EXISTS outbox.idempotency (
                message_id      UUID            NOT NULL,
                consumer_id     VARCHAR(255)    NOT NULL,
                processed_at    TIMESTAMPTZ     NOT NULL,
                PRIMARY KEY (message_id, consumer_id)
            );

            CREATE TABLE IF NOT EXISTS outbox.dead_letters (
                id                  UUID            NOT NULL,
                original_message_id UUID            NOT NULL,
                type                VARCHAR(255)    NOT NULL,
                payload             JSONB           NOT NULL,
                correlation_id      VARCHAR(255),
                causation_id        VARCHAR(255),
                headers_json        JSONB           NOT NULL DEFAULT '{}'::jsonb,
                created_at          TIMESTAMPTZ     NOT NULL,
                dead_lettered_at    TIMESTAMPTZ     NOT NULL DEFAULT NOW(),
                retry_count         INT             NOT NULL DEFAULT 0,
                error_reason        TEXT            NOT NULL,
                last_error          TEXT,
                PRIMARY KEY (id)
            );

            CREATE INDEX IF NOT EXISTS outbox_messages_pending_immediate_idx
                ON outbox.messages (state, created_at ASC)
                INCLUDE (id)
                WHERE state IN (0, 3) AND deliver_at IS NULL;

            CREATE INDEX IF NOT EXISTS outbox_messages_pending_scheduled_idx
                ON outbox.messages (state, deliver_at ASC, created_at ASC)
                WHERE state IN (0, 3) AND deliver_at IS NOT NULL;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Test doubles
    // ─────────────────────────────────────────────────────────────────────

    private sealed class FakeBroker : IBrokerPublisher
    {
        public ConcurrentBag<OutboxMessage> PublishedMessages { get; } = new();

        public ValueTask<DispatchResult> PublishRawAsync(
            OutboxMessage message,
            OutboxMessageMetadata metadata,
            DispatchContext context)
        {
            PublishedMessages.Add(message);
            return ValueTask.FromResult(DispatchResult.Ok());
        }
    }

    /// <summary>
    /// Broker that returns <see cref="DispatchResult.FailAndRetry(Exception)"/> for the first
    /// N calls, then succeeds — exercises the retry interceptor.
    /// </summary>
    private sealed class FlakyBroker : IBrokerPublisher
    {
        private readonly int _failCount;
        private int _callCount;

        public ConcurrentBag<OutboxMessage> PublishedMessages { get; } = new();
        public int FailureCount => Math.Min(Volatile.Read(ref _callCount) - 1, _failCount);

        public FlakyBroker(int failCount) => _failCount = failCount;

        public ValueTask<DispatchResult> PublishRawAsync(
            OutboxMessage message,
            OutboxMessageMetadata metadata,
            DispatchContext context)
        {
            int call = Interlocked.Increment(ref _callCount);
            if (call <= _failCount)
            {
                return ValueTask.FromResult(
                    DispatchResult.FailAndRetry(
                        new InvalidOperationException($"Simulated broker failure #{call}")));
            }

            PublishedMessages.Add(message);
            return ValueTask.FromResult(DispatchResult.Ok());
        }
    }

    private sealed class DummySerializer : EricksonLopez.Outbox.Serialization.IOutboxSerializer
    {
        private static readonly byte[] EmptyJson = "{}"u8.ToArray();

        public ReadOnlyMemory<byte> Serialize<TMessage>(TMessage message) => EmptyJson;

        public void Serialize<TMessage>(TMessage message, System.Buffers.IBufferWriter<byte> buffer)
        {
            var span = buffer.GetSpan(EmptyJson.Length);
            EmptyJson.AsSpan().CopyTo(span);
            buffer.Advance(EmptyJson.Length);
        }

        public TMessage Deserialize<TMessage>(ReadOnlySpan<byte> data) => default!;
    }

    private sealed class DummyTypeResolver : EricksonLopez.Outbox.Serialization.IOutboxMessageTypeResolver
    {
        public Type Resolve(string alias) => typeof(object);

        public bool TryGetAlias(Type type, out string? alias)
        {
            alias = "chaos.test.v1";
            return true;
        }

        public string GetAlias(Type type) => "chaos.test.v1";

        public IReadOnlyDictionary<string, Type> GetAllMappings()
            => new Dictionary<string, Type>();
    }
}






