using System.Globalization;
using Microsoft.Data.Sqlite;
using ProjectTime.Agent.Tracking;
using ProjectTime.Shared;

namespace ProjectTime.Agent.Storage;

/// <summary>
/// Lokal SQLite-buffert. Allt som spåras skrivs hit först och synkas sedan till servern,
/// så att inget går förlorat när datorn är offline.
/// </summary>
public sealed class LocalStore : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly object _lock = new();

    public LocalStore(string directory)
    {
        Directory.CreateDirectory(directory);
        DatabasePath = Path.Combine(directory, "agent.db");
        _conn = new SqliteConnection($"Data Source={DatabasePath}");
        _conn.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("""
            CREATE TABLE IF NOT EXISTS segments (
                client_id TEXT PRIMARY KEY,
                start_utc TEXT NOT NULL,
                end_utc TEXT NOT NULL,
                app TEXT NOT NULL,
                title TEXT NOT NULL,
                path TEXT NULL,
                is_idle INTEGER NOT NULL,
                is_open INTEGER NOT NULL,
                version INTEGER NOT NULL DEFAULT 1,
                synced_version INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS ix_segments_unsynced ON segments(synced_version, version);
            """);
    }

    public string DatabasePath { get; }

    public void Upsert(Segment s)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO segments (client_id, start_utc, end_utc, app, title, path, is_idle, is_open, version, synced_version)
                VALUES ($id, $start, $end, $app, $title, $path, $idle, $open, 1, 0)
                ON CONFLICT(client_id) DO UPDATE SET
                    end_utc = excluded.end_utc,
                    is_open = excluded.is_open,
                    version = segments.version + 1;
                """;
            cmd.Parameters.AddWithValue("$id", s.ClientId.ToString("N"));
            cmd.Parameters.AddWithValue("$start", Fmt(s.StartUtc));
            cmd.Parameters.AddWithValue("$end", Fmt(s.EndUtc));
            cmd.Parameters.AddWithValue("$app", s.AppName);
            cmd.Parameters.AddWithValue("$title", s.WindowTitle);
            cmd.Parameters.AddWithValue("$path", (object?)s.ProcessPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$idle", s.IsIdle ? 1 : 0);
            cmd.Parameters.AddWithValue("$open", s.IsOpen ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    public void Delete(Guid clientId)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM segments WHERE client_id = $id";
            cmd.Parameters.AddWithValue("$id", clientId.ToString("N"));
            cmd.ExecuteNonQuery();
        }
    }

    public sealed record PendingSegment(ActivitySegmentDto Dto, long Version);

    public List<PendingSegment> GetUnsynced(int limit)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                SELECT client_id, start_utc, end_utc, app, title, path, is_idle, is_open, version
                FROM segments WHERE version > synced_version
                ORDER BY start_utc LIMIT $limit
                """;
            cmd.Parameters.AddWithValue("$limit", limit);
            using var r = cmd.ExecuteReader();
            var list = new List<PendingSegment>();
            while (r.Read())
            {
                var dto = new ActivitySegmentDto(
                    Guid.ParseExact(r.GetString(0), "N"),
                    Parse(r.GetString(1)),
                    Parse(r.GetString(2)),
                    r.GetString(3),
                    r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetString(5),
                    r.GetInt32(6) == 1,
                    r.GetInt32(7) == 1);
                list.Add(new PendingSegment(dto, r.GetInt64(8)));
            }
            return list;
        }
    }

    public void MarkSynced(IEnumerable<PendingSegment> items)
    {
        lock (_lock)
        {
            using var tx = _conn.BeginTransaction();
            foreach (var item in items)
            {
                using var cmd = _conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE segments SET synced_version = $v WHERE client_id = $id AND synced_version < $v";
                cmd.Parameters.AddWithValue("$v", item.Version);
                cmd.Parameters.AddWithValue("$id", item.Dto.ClientId.ToString("N"));
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    public int CountUnsynced()
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM segments WHERE version > synced_version";
            return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Tar bort synkade, avslutade segment äldre än angivet antal dagar.</summary>
    public int Purge(int retentionDays)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM segments WHERE is_open = 0 AND version <= synced_version AND end_utc < $cutoff";
            cmd.Parameters.AddWithValue("$cutoff", Fmt(DateTimeOffset.UtcNow.AddDays(-retentionDays)));
            return cmd.ExecuteNonQuery();
        }
    }

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string Fmt(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string s) => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    public void Dispose() => _conn.Dispose();
}
