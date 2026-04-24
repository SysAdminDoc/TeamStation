using TeamStation.Core.Models;
using TeamStation.Data.Security;
using TeamStation.Data.Storage;

namespace TeamStation.Tests;

/// <summary>
/// End-to-end repository tests against a real on-disk SQLite DB in a temp
/// directory. Each test gets its own DB file so they can run in parallel.
/// </summary>
public class DatabaseIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly Database _db;
    private readonly CryptoService _crypto;
    private readonly EntryRepository _entries;
    private readonly FolderRepository _folders;
    private readonly SessionRepository _sessions;
    private readonly AuditLogRepository _audit;

    public DatabaseIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"ts-test-{Guid.NewGuid():N}.db");
        _db = new Database(_dbPath);
        _crypto = CryptoService.CreateOrLoad(_db);
        _entries = new EntryRepository(_db, _crypto);
        _folders = new FolderRepository(_db, _crypto);
        _sessions = new SessionRepository(_db);
        _audit = new AuditLogRepository(_db);
    }

    public void Dispose()
    {
        // SQLite connection pool may still hold the file; drain it before deleting.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            try { if (File.Exists(_dbPath + suffix)) File.Delete(_dbPath + suffix); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Schema_initializes_on_fresh_db()
    {
        Assert.True(File.Exists(_dbPath));
        Assert.Empty(_folders.GetAll());
        Assert.Empty(_entries.GetAll());
    }

    [Fact]
    public void Upsert_and_GetAll_round_trip_a_folder()
    {
        var f = new Folder { Name = "Site A", AccentColor = "#F9E2AF", DefaultPassword = "pw" };
        _folders.Upsert(f);
        var loaded = Assert.Single(_folders.GetAll());
        Assert.Equal(f.Id, loaded.Id);
        Assert.Equal("Site A", loaded.Name);
        Assert.Equal("#F9E2AF", loaded.AccentColor);
        Assert.Equal("pw", loaded.DefaultPassword); // decrypted on read
    }

    [Fact]
    public void Upsert_encrypts_password_and_GetAll_decrypts_it()
    {
        var e = new ConnectionEntry
        {
            Name = "E", TeamViewerId = "123456789",
            Password = "super-secret",
        };
        _entries.Upsert(e);
        var loaded = Assert.Single(_entries.GetAll());
        Assert.Equal("super-secret", loaded.Password);
    }

    [Fact]
    public void Upsert_updates_in_place_on_conflicting_id()
    {
        var e = new ConnectionEntry
        {
            Name = "Original", TeamViewerId = "123456789", Password = "v1",
        };
        _entries.Upsert(e);

        e.Name = "Updated";
        e.Password = "v2";
        _entries.Upsert(e);

        var loaded = Assert.Single(_entries.GetAll());
        Assert.Equal("Updated", loaded.Name);
        Assert.Equal("v2", loaded.Password);
    }

    [Fact]
    public void Delete_removes_the_entry()
    {
        var e = new ConnectionEntry { Name = "E", TeamViewerId = "123456789" };
        _entries.Upsert(e);
        _entries.Delete(e.Id);
        Assert.Empty(_entries.GetAll());
    }

    [Fact]
    public void Deleting_a_folder_leaves_its_entries_unassigned_not_broken()
    {
        var folder = new Folder { Name = "F" };
        _folders.Upsert(folder);
        var e = new ConnectionEntry
        {
            Name = "E", TeamViewerId = "123456789", ParentFolderId = folder.Id,
        };
        _entries.Upsert(e);

        _folders.Delete(folder.Id);

        var loaded = Assert.Single(_entries.GetAll());
        Assert.Null(loaded.ParentFolderId); // FK ON DELETE SET NULL
    }

    [Fact]
    public void Nullable_enum_columns_round_trip_correctly()
    {
        var e = new ConnectionEntry
        {
            Name = "E", TeamViewerId = "123456789",
            Mode = null, Quality = null, AccessControl = null,
        };
        _entries.Upsert(e);
        var loaded = Assert.Single(_entries.GetAll());
        Assert.Null(loaded.Mode);
        Assert.Null(loaded.Quality);
        Assert.Null(loaded.AccessControl);
    }

    [Fact]
    public void Tags_round_trip_through_csv_encoding()
    {
        var e = new ConnectionEntry
        {
            Name = "E", TeamViewerId = "123456789",
            Tags = new List<string> { "a", "b", "c" },
        };
        _entries.Upsert(e);
        var loaded = Assert.Single(_entries.GetAll());
        Assert.Equal(new[] { "a", "b", "c" }, loaded.Tags);
    }

    [Fact]
    public void Version_3_entry_fields_round_trip()
    {
        var e = new ConnectionEntry
        {
            Name = "Pinned", TeamViewerId = "123456789",
            ProfileName = "After hours",
            TeamViewerPathOverride = @"C:\Tools\TeamViewer.exe",
            IsPinned = true,
            WakeMacAddress = "AA-BB-CC-DD-EE-FF",
            WakeBroadcastAddress = "192.168.4.255",
            PreLaunchScript = "Write-Output before",
            PostLaunchScript = "Write-Output after",
        };

        _entries.Upsert(e);

        var loaded = Assert.Single(_entries.GetAll());
        Assert.Equal("After hours", loaded.ProfileName);
        Assert.Equal(@"C:\Tools\TeamViewer.exe", loaded.TeamViewerPathOverride);
        Assert.True(loaded.IsPinned);
        Assert.Equal("AA-BB-CC-DD-EE-FF", loaded.WakeMacAddress);
        Assert.Equal("192.168.4.255", loaded.WakeBroadcastAddress);
        Assert.Equal("Write-Output before", loaded.PreLaunchScript);
        Assert.Equal("Write-Output after", loaded.PostLaunchScript);
    }

    [Fact]
    public void Version_3_folder_fields_round_trip()
    {
        var f = new Folder
        {
            Name = "Site scripts",
            DefaultTeamViewerPath = @"D:\Apps\TeamViewer.exe",
            DefaultWakeBroadcastAddress = "10.0.0.255",
            PreLaunchScript = "Write-Output folder-before",
            PostLaunchScript = "Write-Output folder-after",
        };

        _folders.Upsert(f);

        var loaded = Assert.Single(_folders.GetAll());
        Assert.Equal(@"D:\Apps\TeamViewer.exe", loaded.DefaultTeamViewerPath);
        Assert.Equal("10.0.0.255", loaded.DefaultWakeBroadcastAddress);
        Assert.Equal("Write-Output folder-before", loaded.PreLaunchScript);
        Assert.Equal("Write-Output folder-after", loaded.PostLaunchScript);
    }

    [Fact]
    public void TouchLastConnected_updates_only_the_target_row()
    {
        var a = new ConnectionEntry { Name = "A", TeamViewerId = "111111111" };
        var b = new ConnectionEntry { Name = "B", TeamViewerId = "222222222" };
        _entries.Upsert(a);
        _entries.Upsert(b);

        var when = DateTimeOffset.UtcNow;
        _entries.TouchLastConnected(a.Id, when);

        var all = _entries.GetAll();
        var touched = all.First(e => e.Id == a.Id);
        var untouched = all.First(e => e.Id == b.Id);
        Assert.NotNull(touched.LastConnectedUtc);
        Assert.Null(untouched.LastConnectedUtc);
    }

    [Fact]
    public void Session_history_records_completion_and_exports_csv()
    {
        var started = DateTimeOffset.UtcNow.AddMinutes(-7);
        var session = new SessionRecord
        {
            EntryName = "Office, Desk",
            TeamViewerId = "123456789",
            ProfileName = "Default",
            Mode = ConnectionMode.RemoteControl,
            Route = "URI",
            StartedUtc = started,
            Outcome = "Started",
        };

        _sessions.Upsert(session);
        _sessions.Complete(session.Id, started.AddMinutes(7), "Exited 0");

        var loaded = Assert.Single(_sessions.GetRecent());
        Assert.Equal("Exited 0", loaded.Outcome);
        Assert.Equal(7, loaded.Duration?.TotalMinutes);

        var csvPath = Path.Combine(Path.GetTempPath(), $"ts-sessions-{Guid.NewGuid():N}.csv");
        try
        {
            _sessions.ExportCsv(csvPath);
            var text = File.ReadAllText(csvPath);
            Assert.Contains("\"Office, Desk\"", text);
            Assert.Contains("Exited 0", text);
        }
        finally
        {
            try { if (File.Exists(csvPath)) File.Delete(csvPath); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Audit_log_returns_recent_events_newest_first()
    {
        var target = Guid.NewGuid();
        _audit.Append(new AuditEvent
        {
            OccurredUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            Action = "create",
            TargetType = "connection",
            TargetId = target,
            Summary = "Created connection.",
        });
        _audit.Append(new AuditEvent
        {
            OccurredUtc = DateTimeOffset.UtcNow,
            Action = "launch",
            TargetType = "connection",
            TargetId = target,
            Summary = "Launched connection.",
        });

        var recent = _audit.GetRecent();
        Assert.Equal(["launch", "create"], recent.Select(e => e.Action).ToArray());
        Assert.All(recent, e => Assert.Equal(target, e.TargetId));
    }
}
