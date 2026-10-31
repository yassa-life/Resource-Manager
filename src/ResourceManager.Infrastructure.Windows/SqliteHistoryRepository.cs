using Microsoft.Data.Sqlite;
using ResourceManager.Core;

namespace ResourceManager.Infrastructure.Windows;

public sealed class SqliteHistoryRepository(string databasePath) : IHistoryRepository
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS resource_samples(
              timestamp TEXT NOT NULL PRIMARY KEY,
              cpu REAL NOT NULL,
              memory_total INTEGER NOT NULL,
              memory_used INTEGER NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask AppendAsync(ResourceSample sample, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO resource_samples VALUES($at,$cpu,$total,$used)";
        command.Parameters.AddWithValue("$at", sample.Timestamp.ToString("O"));
        command.Parameters.AddWithValue("$cpu", sample.CpuPercent);
        command.Parameters.AddWithValue("$total", sample.PhysicalMemoryTotal);
        command.Parameters.AddWithValue("$used", sample.PhysicalMemoryUsed);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ResourceSample>> ReadRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        var results = new List<ResourceSample>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT timestamp,cpu,memory_total,memory_used FROM resource_samples ORDER BY timestamp DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(new(DateTimeOffset.Parse(reader.GetString(0)), reader.GetDouble(1), reader.GetInt64(2), reader.GetInt64(3), []));
        return results;
    }

    public async ValueTask PruneAsync(TimeSpan retention, long maximumBytes, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM resource_samples WHERE timestamp < $cutoff";
        command.Parameters.AddWithValue("$cutoff", DateTimeOffset.Now.Subtract(retention).ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (new FileInfo(databasePath).Length > maximumBytes)
        {
            command.CommandText = "DELETE FROM resource_samples WHERE timestamp IN (SELECT timestamp FROM resource_samples ORDER BY timestamp LIMIT (SELECT COUNT(*)/4 FROM resource_samples))";
            command.Parameters.Clear();
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM resource_samples";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
