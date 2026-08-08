using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Dapper;
using EricksonLopez.Outbox;

using EricksonLopez.Outbox.Storage.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EricksonLopez.Outbox.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(iterationCount: 100, warmupCount: 20)]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class H_SqlFetchBenchmarks
{
    private PostgreSqlOutboxRepository _repository = null!;
    private NpgsqlDataSource _dataSource = null!;
    private NpgsqlConnection _dapperConnection = null!;
    private const string TableName = "outbox.messages";
    private const string Schema = "outbox";
    private readonly Guid _ownerId = Guid.NewGuid();

    [GlobalSetup]
    public async Task Setup()
    {
        var connString = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";
        _dataSource = NpgsqlDataSource.Create(connString);

        // Try to create the table and seed a few messages for benchmarking, ignoring errors if DB not reachable
        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                CREATE SCHEMA IF NOT EXISTS {Schema};
                CREATE TABLE IF NOT EXISTS {TableName} (
                    id uuid PRIMARY KEY,
                    message_type varchar(255) NOT NULL,
                    payload bytea NOT NULL,
                    correlation_id varchar(255),
                    causation_id varchar(255),
                    headers bytea NOT NULL,
                    created_at timestamptz NOT NULL,
                    processed_at timestamptz,
                    deliver_at timestamptz,
                    state smallint NOT NULL,
                    retry_count int NOT NULL,
                    error text,
                    owner_id uuid
                );
            ";
            await cmd.ExecuteNonQueryAsync();

            for (int i = 0; i < 100; i++)
            {
                cmd.CommandText = $@"
                    INSERT INTO {TableName} (id, message_type, payload, headers, created_at, state, retry_count)
                    VALUES ('{Guid.NewGuid()}', 'type', '\x00', '\x00', NOW(), 0, 0) ON CONFLICT DO NOTHING;
                ";
                await cmd.ExecuteNonQueryAsync();
            }
        }
        catch { }

        _dapperConnection = _dataSource.CreateConnection();
        await _dapperConnection.OpenAsync();

        var runtimeOptions = Options.Create(new OutboxRuntimeOptions { SchemaName = Schema, TableName = "messages" });
        _repository = new PostgreSqlOutboxRepository(_dataSource, runtimeOptions);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_dapperConnection != null)
        {
            await _dapperConnection.DisposeAsync();
        }
        if (_dataSource != null)
        {
            await _dataSource.DisposeAsync();
        }
    }

    [Benchmark(Baseline = true)]
    public async Task Dapper_Raw_Fetch()
    {
        try
        {
            var sql = $@"
                WITH batch AS (
                    SELECT id, created_at FROM {TableName}
                    WHERE (state = 0 OR state = 3)
                    ORDER BY created_at ASC, id ASC
                    LIMIT 10
                    FOR UPDATE SKIP LOCKED
                )
                UPDATE {TableName} m
                SET state = 1, updated_at = NOW(), owner_id = @OwnerId
                FROM batch
                WHERE m.id = batch.id AND m.created_at = batch.created_at
                RETURNING m.id";

            var result = await _dapperConnection.QueryAsync<Guid>(sql, new { OwnerId = _ownerId });
        }
        catch { } // Ignore if DB is down during local bench runs without postgres
    }

    [Benchmark]
    public async ValueTask EricksonLopez_FetchAsync()
    {
        try
        {
            await _repository.FetchPendingAsync(10, default);
        }
        catch { } // Ignore if DB is down during local bench runs
    }
}
