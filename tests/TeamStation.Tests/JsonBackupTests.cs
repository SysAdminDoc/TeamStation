using TeamStation.Core.Models;
using TeamStation.Core.Serialization;

namespace TeamStation.Tests;

public class JsonBackupTests
{
    [Fact]
    public void RoundTrip_preserves_every_significant_field()
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
        };

        var entry = new ConnectionEntry
        {
            ParentFolderId = folder.Id,
            Name = "Reception PC",
            TeamViewerId = "123456789",
            Password = "entry-level-pw",
            Mode = null, // inherit
            Quality = ConnectionQuality.OptimizeQuality,
            AccessControl = null,
            Proxy = new ProxySettings("proxy.internal", 3128, "u", "p"),
            Notes = "Front-of-house kiosk",
            Tags = new List<string> { "kiosk", "lobby" },
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

        var e = Assert.Single(entries);
        Assert.Equal(entry.Id, e.Id);
        Assert.Equal(entry.ParentFolderId, e.ParentFolderId);
        Assert.Equal(entry.TeamViewerId, e.TeamViewerId);
        Assert.Equal(entry.Password, e.Password);
        Assert.Null(e.Mode);
        Assert.Equal(entry.Quality, e.Quality);
        Assert.Null(e.AccessControl);
        Assert.NotNull(e.Proxy);
        Assert.Equal("proxy.internal", e.Proxy!.Host);
        Assert.Equal(3128, e.Proxy!.Port);
        Assert.Equal("u", e.Proxy!.Username);
        Assert.Equal("p", e.Proxy!.Password);
        Assert.Equal(new[] { "kiosk", "lobby" }, e.Tags);
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
    [InlineData("""{"formatVersion":1,"exportedAtUtc":"2026-04-23T00:00:00+00:00"}""")]
    [InlineData("""{"formatVersion":1,"exportedAtUtc":"2026-04-23T00:00:00+00:00","folders":null,"entries":null}""")]
    [InlineData("""{"formatVersion":1,"exportedAtUtc":"2026-04-23T00:00:00+00:00","folders":[]}""")]
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
            ParentFolderId = ghostFolderId, // references a folder not in the export
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
