using Microsoft.Data.Sqlite;
using TeamStation.Core.Models;
using TeamStation.Data.Security;
using TeamStation.Data.Storage;

namespace TeamStation.Tests;

/// <summary>
/// CloudMirrorService relies on SQLite VACUUM INTO to produce a consistent
/// snapshot of a live WAL-mode database. These tests prove the snapshot is a
/// valid SQLite file and that it captures every row committed before the call.
/// </summary>
public class CloudMirrorServiceTests : IDisposable
{
    private readonly string _sourceDir;
    private readonly string _mirrorDir;
    private readonly string _dbPath;
    private readonly Database _db;
    private readonly CryptoService _crypto;
    private readonly EntryRepository _entries;

    public CloudMirrorServiceTests()
    {
        _sourceDir = Path.Combine(Path.GetTempPath(), $"ts-src-{Guid.NewGuid():N}");
        _mirrorDir = Path.Combine(Path.GetTempPath(), $"ts-mirror-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sourceDir);
        _dbPath = Path.Combine(_sourceDir, "teamstation.db");
        _db = new Database(_dbPath);
        _crypto = CryptoService.CreateOrLoad(_db);
        _entries = new EntryRepository(_db, _crypto);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var dir in new[] { _sourceDir, _mirrorDir })
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    [Fact]
    public void MirrorDatabase_returns_null_when_source_is_missing()
    {
        var result = CloudMirrorService.MirrorDatabase(
            Path.Combine(_sourceDir, "does-not-exist.db"),
            _mirrorDir);
        Assert.Null(result);
        Assert.False(Directory.Exists(_mirrorDir) && Directory.EnumerateFiles(_mirrorDir).Any());
    }

    [Fact]
    public void MirrorDatabase_noops_when_destination_is_blank()
    {
        Assert.Null(CloudMirrorService.MirrorDatabase(_dbPath, null));
        Assert.Null(CloudMirrorService.MirrorDatabase(_dbPath, "   "));
    }

    [Fact]
    public void MirrorDatabase_produces_a_valid_single_file_snapshot()
    {
        _entries.Upsert(new ConnectionEntry { Name = "A", TeamViewerId = "123456789", Password = "p1" });
        _entries.Upsert(new ConnectionEntry { Name = "B", TeamViewerId = "222222222", Password = "p2" });

        var mirrorPath = CloudMirrorService.MirrorDatabase(_dbPath, _mirrorDir);

        Assert.NotNull(mirrorPath);
        Assert.True(File.Exists(mirrorPath));
        // VACUUM INTO must NOT produce sidecars — the whole point is a single consistent file.
        Assert.False(File.Exists(mirrorPath + "-wal"));
        Assert.False(File.Exists(mirrorPath + "-shm"));

        // And the mirror must open cleanly and contain everything the source had at call time.
        using var c = new SqliteConnection($"Data Source={mirrorPath};Mode=ReadOnly");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM entries;";
        var count = Convert.ToInt64(cmd.ExecuteScalar());
        Assert.Equal(2, count);
    }

    [Fact]
    public void MirrorDatabase_overwrites_previous_mirror_atomically()
    {
        _entries.Upsert(new ConnectionEntry { Name = "A", TeamViewerId = "123456789" });
        var first = CloudMirrorService.MirrorDatabase(_dbPath, _mirrorDir);
        Assert.NotNull(first);

        _entries.Upsert(new ConnectionEntry { Name = "B", TeamViewerId = "222222222" });
        var second = CloudMirrorService.MirrorDatabase(_dbPath, _mirrorDir);

        Assert.Equal(first, second);

        using var c = new SqliteConnection($"Data Source={second};Mode=ReadOnly");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM entries;";
        Assert.Equal(2L, Convert.ToInt64(cmd.ExecuteScalar()));
    }
}
