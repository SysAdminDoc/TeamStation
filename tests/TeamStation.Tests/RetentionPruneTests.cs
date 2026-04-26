using Microsoft.Data.Sqlite;
using TeamStation.Core.Models;
using TeamStation.Data.Security;
using TeamStation.Data.Storage;

namespace TeamStation.Tests;

/// <summary>
/// Verifies that session history and audit log pruning delete rows strictly
/// older than the retention window and leave fresher rows untouched.
/// </summary>
public class RetentionPruneTests : IDisposable
{
    private readonly string _dbPath;
    private readonly Database _db;
    private readonly SessionRepository _sessions;
    private readonly AuditLogRepository _audit;

    public RetentionPruneTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"ts-prune-{Guid.NewGuid():N}.db");
        _db = new Database(_dbPath);
        var crypto = CryptoService.CreateOrLoad(_db);
        _sessions = new SessionRepository(_db);
        _audit = new AuditLogRepository(_db, crypto);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(_dbPath + suffix)) File.Delete(_dbPath + suffix); }
            catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Sessions_Prune_removes_only_rows_older_than_retention()
    {
        var retention = TimeSpan.FromDays(30);
        _sessions.Upsert(new SessionRecord
        {
            EntryName = "Old",
            TeamViewerId = "111111111",
            Route = "CLI",
            StartedUtc = DateTimeOffset.UtcNow - TimeSpan.FromDays(60),
        });
        _sessions.Upsert(new SessionRecord
        {
            EntryName = "Fresh",
            TeamViewerId = "222222222",
            Route = "CLI",
            StartedUtc = DateTimeOffset.UtcNow - TimeSpan.FromDays(5),
        });

        var removed = _sessions.Prune(retention);

        Assert.Equal(1, removed);
        var remaining = _sessions.GetRecent();
        Assert.Single(remaining);
        Assert.Equal("Fresh", remaining[0].EntryName);
    }

    [Fact]
    public void Sessions_Prune_with_non_positive_retention_is_a_noop()
    {
        _sessions.Upsert(new SessionRecord
        {
            EntryName = "Keep",
            TeamViewerId = "111111111",
            Route = "CLI",
            StartedUtc = DateTimeOffset.UtcNow - TimeSpan.FromDays(9999),
        });
        Assert.Equal(0, _sessions.Prune(TimeSpan.Zero));
        Assert.Equal(0, _sessions.Prune(TimeSpan.FromDays(-1)));
        Assert.Single(_sessions.GetRecent());
    }

    [Fact]
    public void Audit_Prune_removes_only_rows_older_than_retention()
    {
        var retention = TimeSpan.FromDays(30);
        _audit.Append(new AuditEvent
        {
            OccurredUtc = DateTimeOffset.UtcNow - TimeSpan.FromDays(90),
            Action = "old",
            TargetType = "connection",
            Summary = "stale",
        });
        _audit.Append(new AuditEvent
        {
            OccurredUtc = DateTimeOffset.UtcNow - TimeSpan.FromHours(1),
            Action = "fresh",
            TargetType = "connection",
            Summary = "recent",
        });

        var removed = _audit.Prune(retention);

        Assert.Equal(1, removed);
        var remaining = _audit.GetRecent();
        Assert.Single(remaining);
        Assert.Equal("fresh", remaining[0].Action);
    }
}
