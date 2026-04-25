using TeamStation.Core.Models;
using TeamStation.Core.Serialization;

namespace TeamStation.Tests;

public class JsonBackupTests
{
    [Fact]
    public void RoundTrip_preserves_every_persisted_field_on_entries_and_folders()
    {
        var folder = new Folder
        {
            Name = "Customer A",
            AccentColor = "#F9E2AF",
            SortOrder = 5,
            DefaultMode = ConnectionMode.FileTransfer,
            DefaultQuality = ConnectionQuality.OptimizeSpeed,
            DefaultAccessControl = AccessControl.ConfirmAll,
            DefaultPassword = "folder-level-pw",
            DefaultTeamViewerPath = @"C:\Custom\TeamViewer.exe",
            DefaultWakeBroadcastAddress = "10.0.0.255",
            PreLaunchScript = "Write-Output folder-pre",
            PostLaunchScript = "Write-Output folder-post",
        };

        var entry = new ConnectionEntry
        {
            ParentFolderId = folder.Id,
            Name = "Reception PC",
            TeamViewerId = "123456789",
            ProfileName = "Front desk",
            Password = "entry-level-pw",
            Mode = null, // inherit
            Quality = ConnectionQuality.OptimizeQuality,
            AccessControl = null,
            Proxy = new ProxySettings("proxy.internal", 3128, "u", "p"),
            TeamViewerPathOverride = @"D:\Alt\TeamViewer.exe",
            IsPinned = true,
            WakeMacAddress = "AA-BB-CC-DD-EE-FF",
            WakeBroadcastAddress = "192.168.1.255",
            PreLaunchScript = "Write-Output entry-pre",
            PostLaunchScript = "Write-Output entry-post",
            Notes = "Front-of-house kiosk",
            Tags = new List<string> { "kiosk", "lobby" },
            LastConnectedUtc = new DateTimeOffset(2026, 3, 1, 10, 30, 0, TimeSpan.Zero),
        };

        var json = JsonBackup.Build(new[] { folder }, new[] { entry });
        var (folders, entries) = JsonBackup.Parse(json);

        var f = Assert.Single(folders);
        Assert.Equal(folder.Id, f.Id);
        Assert.Equal(folder.Name, f.Name);
        Assert.Equal(folder.AccentColor, f.AccentColor);
        Assert.Equal(folder.SortOrder, f.SortOrder);
        Assert.Equal(folder.DefaultMode, f.DefaultMode);
        Assert.Equal(folder.DefaultQuality, f.DefaultQuality);
        Assert.Equal(folder.DefaultAccessControl, f.DefaultAccessControl);
        Assert.Equal(folder.DefaultPassword, f.DefaultPassword);
        Assert.Equal(folder.DefaultTeamViewerPath, f.DefaultTeamViewerPath);
        Assert.Equal(folder.DefaultWakeBroadcastAddress, f.DefaultWakeBroadcastAddress);
        Assert.Equal(folder.PreLaunchScript, f.PreLaunchScript);
        Assert.Equal(folder.PostLaunchScript, f.PostLaunchScript);

        var e = Assert.Single(entries);
        Assert.Equal(entry.Id, e.Id);
        Assert.Equal(entry.ParentFolderId, e.ParentFolderId);
        Assert.Equal(entry.Name, e.Name);
        Assert.Equal(entry.TeamViewerId, e.TeamViewerId);
        Assert.Equal(entry.ProfileName, e.ProfileName);
        Assert.Equal(entry.Password, e.Password);
        Assert.Null(e.Mode);
        Assert.Equal(entry.Quality, e.Quality);
        Assert.Null(e.AccessControl);
        Assert.NotNull(e.Proxy);
        Assert.Equal("proxy.internal", e.Proxy!.Host);
        Assert.Equal(3128, e.Proxy!.Port);
        Assert.Equal("u", e.Proxy!.Username);
        Assert.Equal("p", e.Proxy!.Password);
        Assert.Equal(entry.TeamViewerPathOverride, e.TeamViewerPathOverride);
        Assert.True(e.IsPinned);
        Assert.Equal(entry.WakeMacAddress, e.WakeMacAddress);
        Assert.Equal(entry.WakeBroadcastAddress, e.WakeBroadcastAddress);
        Assert.Equal(entry.PreLaunchScript, e.PreLaunchScript);
        Assert.Equal(entry.PostLaunchScript, e.PostLaunchScript);
        Assert.Equal(entry.Notes, e.Notes);
        Assert.Equal(entry.Tags, e.Tags);
        Assert.Equal(entry.LastConnectedUtc, e.LastConnectedUtc);
    }

    // v1 files (v0.1.x) didn't carry profile, pin, override path, WOL or scripts.
    // Parsing them must still succeed and populate sensible defaults.
    [Fact]
    public void Parse_accepts_format_version_1_files_without_newer_fields()
    {
        var v1Json = """
                     {
                       "formatVersion": 1,
                       "exportedAtUtc": "2026-01-01T00:00:00+00:00",
                       "folders": [
                         { "id": "11111111-1111-1111-1111-111111111111", "name": "F", "sortOrder": 0 }
                       ],
                       "entries": [
                         {
                           "id": "22222222-2222-2222-2222-222222222222",
                           "name": "E",
                           "teamViewerId": "123456789",
                           "tags": []
                         }
                       ]
                     }
                     """;
        var (folders, entries) = JsonBackup.Parse(v1Json);
        var f = Assert.Single(folders);
        Assert.Null(f.DefaultTeamViewerPath);
        Assert.Null(f.DefaultWakeBroadcastAddress);
        Assert.Null(f.PreLaunchScript);
        Assert.Null(f.PostLaunchScript);

        var e = Assert.Single(entries);
        Assert.Equal("Default", e.ProfileName);
        Assert.False(e.IsPinned);
        Assert.Null(e.TeamViewerPathOverride);
        Assert.Null(e.WakeMacAddress);
        Assert.Null(e.PreLaunchScript);
        Assert.Null(e.PostLaunchScript);
    }

    [Fact]
    public void Parse_empty_string_throws_invalid_data()
    {
        Assert.Throws<InvalidDataException>(() => JsonBackup.Parse(""));
        Assert.Throws<InvalidDataException>(() => JsonBackup.Parse("   "));
    }

    // Covers the v0.1.1 fix: we must tolerate JSON whose `folders` / `entries`
    // arrays are absent or null. Previously Parse() would NullReferenceException.
    [Theory]
    [InlineData("""{"formatVersion":2,"exportedAtUtc":"2026-04-23T00:00:00+00:00"}""")]
    [InlineData("""{"formatVersion":2,"exportedAtUtc":"2026-04-23T00:00:00+00:00","folders":null,"entries":null}""")]
    [InlineData("""{"formatVersion":2,"exportedAtUtc":"2026-04-23T00:00:00+00:00","folders":[]}""")]
    public void Parse_tolerates_missing_or_null_collections(string json)
    {
        var (folders, entries) = JsonBackup.Parse(json);
        Assert.Empty(folders);
        Assert.Empty(entries);
    }

    [Fact]
    public void Parse_throws_on_malformed_json_with_helpful_message()
    {
        var ex = Assert.Throws<InvalidDataException>(() => JsonBackup.Parse("{ this is not json"));
        Assert.Contains("malformed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_rejects_format_version_newer_than_supported()
    {
        var json = $$$"""{"formatVersion": 999, "exportedAtUtc": "2026-04-23T00:00:00+00:00", "folders": [], "entries": []}""";
        var ex = Assert.Throws<InvalidDataException>(() => JsonBackup.Parse(json));
        Assert.Contains("999", ex.Message);
    }

    [Fact]
    public void Parse_regenerates_empty_or_duplicate_entity_ids_before_import()
    {
        var duplicateFolderId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        var duplicateEntryId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
        var json = $$"""
                     {
                       "formatVersion": 2,
                       "exportedAtUtc": "2026-04-23T00:00:00+00:00",
                       "folders": [
                         { "id": "00000000-0000-0000-0000-000000000000", "name": "Missing folder id", "sortOrder": 0 },
                         { "id": "{{duplicateFolderId}}", "name": "Folder A", "sortOrder": 1 },
                         { "id": "{{duplicateFolderId}}", "name": "Folder B", "sortOrder": 2 }
                       ],
                       "entries": [
                         { "id": "00000000-0000-0000-0000-000000000000", "name": "Missing entry id", "teamViewerId": "123456789" },
                         { "id": "{{duplicateEntryId}}", "name": "Entry A", "teamViewerId": "223456789" },
                         { "id": "{{duplicateEntryId}}", "name": "Entry B", "teamViewerId": "323456789" }
                       ]
                     }
                     """;

        var (folders, entries) = JsonBackup.Parse(json);

        Assert.Equal(3, folders.Count);
        Assert.Equal(3, entries.Count);
        Assert.All(folders, folder => Assert.NotEqual(Guid.Empty, folder.Id));
        Assert.All(entries, entry => Assert.NotEqual(Guid.Empty, entry.Id));
        Assert.Equal(3, folders.Select(folder => folder.Id).Distinct().Count());
        Assert.Equal(3, entries.Select(entry => entry.Id).Distinct().Count());
    }

    [Fact]
    public void Parse_rejects_backup_entries_with_non_ascii_TeamViewer_ids()
    {
        var json = """
                   {
                     "formatVersion": 2,
                     "exportedAtUtc": "2026-04-23T00:00:00+00:00",
                     "folders": [],
                     "entries": [
                       {
                         "id": "22222222-2222-2222-2222-222222222222",
                         "name": "Looks numeric",
                         "teamViewerId": "\u0661\u0662\u0663\u0664\u0665\u0666\u0667\u0668\u0669"
                       }
                     ]
                   }
                   """;

        var ex = Assert.Throws<InvalidDataException>(() => JsonBackup.Parse(json));

        Assert.Contains("invalid TeamViewer ID", ex.Message);
        Assert.Contains("ASCII", ex.Message);
    }

    // Covers the v0.1.1 fix: dangling ParentFolderId references should be
    // re-homed to root rather than tripping a FK violation at import time.
    [Fact]
    public void Parse_drops_parent_references_that_are_not_in_the_export()
    {
        var ghostFolderId = Guid.NewGuid();
        var validFolder = new Folder { Name = "Real" };
        var entry = new ConnectionEntry
        {
            Name = "Orphan", TeamViewerId = "123456789",
            ParentFolderId = ghostFolderId,
        };
        var folderWithGhostParent = new Folder
        {
            Name = "Nested",
            ParentFolderId = ghostFolderId,
        };
        var json = JsonBackup.Build(new[] { validFolder, folderWithGhostParent }, new[] { entry });
        var (folders, entries) = JsonBackup.Parse(json);

        Assert.All(folders, f => Assert.True(f.ParentFolderId is null || folders.Any(other => other.Id == f.ParentFolderId)));
        Assert.All(entries, e => Assert.True(e.ParentFolderId is null || folders.Any(f => f.Id == e.ParentFolderId)));
    }
}
