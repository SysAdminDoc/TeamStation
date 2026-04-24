using TeamStation.Core.Models;
using TeamStation.Core.Services;

namespace TeamStation.Tests;

public class InheritanceResolverTests
{
    [Fact]
    public void Resolve_returns_entry_unchanged_when_all_fields_set()
    {
        var entry = new ConnectionEntry
        {
            Name = "A",
            TeamViewerId = "123456789",
            Password = "direct",
            Mode = ConnectionMode.FileTransfer,
            Quality = ConnectionQuality.OptimizeSpeed,
            AccessControl = AccessControl.ConfirmAll,
        };
        var resolved = InheritanceResolver.Resolve(entry, new Dictionary<Guid, Folder>());
        Assert.Equal("direct", resolved.Password);
        Assert.Equal(ConnectionMode.FileTransfer, resolved.Mode);
        Assert.Equal(ConnectionQuality.OptimizeSpeed, resolved.Quality);
        Assert.Equal(AccessControl.ConfirmAll, resolved.AccessControl);
    }

    [Fact]
    public void Resolve_pulls_password_from_nearest_ancestor_with_default()
    {
        var grandparent = new Folder { Name = "Root", DefaultPassword = "gp-pw" };
        var parent = new Folder { Name = "Child", ParentFolderId = grandparent.Id }; // no password
        var entry = new ConnectionEntry
        {
            Name = "E", TeamViewerId = "123456789", ParentFolderId = parent.Id,
        };
        var folders = new Dictionary<Guid, Folder>
        {
            [grandparent.Id] = grandparent,
            [parent.Id] = parent,
        };

        var resolved = InheritanceResolver.Resolve(entry, folders);

        Assert.Equal("gp-pw", resolved.Password);
    }

    [Fact]
    public void Resolve_prefers_closer_ancestor_when_both_carry_default()
    {
        var grandparent = new Folder { Name = "Root", DefaultMode = ConnectionMode.Vpn };
        var parent = new Folder
        {
            Name = "Child",
            ParentFolderId = grandparent.Id,
            DefaultMode = ConnectionMode.FileTransfer,
        };
        var entry = new ConnectionEntry
        {
            Name = "E", TeamViewerId = "123456789", ParentFolderId = parent.Id,
        };
        var folders = new Dictionary<Guid, Folder>
        {
            [grandparent.Id] = grandparent,
            [parent.Id] = parent,
        };

        var resolved = InheritanceResolver.Resolve(entry, folders);

        Assert.Equal(ConnectionMode.FileTransfer, resolved.Mode); // closer wins
    }

    [Fact]
    public void Resolve_pulls_launch_path_wake_broadcast_and_scripts_from_folder()
    {
        var folder = new Folder
        {
            Name = "F",
            DefaultTeamViewerPath = @"C:\TeamViewer\TeamViewer.exe",
            DefaultWakeBroadcastAddress = "192.168.1.255",
            PreLaunchScript = "Write-Output pre",
            PostLaunchScript = "Write-Output post",
        };
        var entry = new ConnectionEntry
        {
            Name = "E",
            TeamViewerId = "123456789",
            ParentFolderId = folder.Id,
            WakeMacAddress = "00:11:22:33:44:55",
        };

        var resolved = InheritanceResolver.Resolve(entry, new Dictionary<Guid, Folder> { [folder.Id] = folder });

        Assert.Equal(@"C:\TeamViewer\TeamViewer.exe", resolved.TeamViewerPathOverride);
        Assert.Equal("192.168.1.255", resolved.WakeBroadcastAddress);
        Assert.Equal("Write-Output pre", resolved.PreLaunchScript);
        Assert.Equal("Write-Output post", resolved.PostLaunchScript);
        Assert.Null(entry.TeamViewerPathOverride);
        Assert.Null(entry.WakeBroadcastAddress);
    }

    [Fact]
    public void Resolve_does_not_mutate_source_entry()
    {
        var folder = new Folder { Name = "F", DefaultPassword = "from-folder" };
        var entry = new ConnectionEntry
        {
            Name = "E", TeamViewerId = "123456789", ParentFolderId = folder.Id,
        };
        var folders = new Dictionary<Guid, Folder> { [folder.Id] = folder };

        var resolved = InheritanceResolver.Resolve(entry, folders);

        Assert.Equal("from-folder", resolved.Password);
        Assert.Null(entry.Password); // source stays null — inherit semantics preserved
    }

    [Fact]
    public void Resolve_returns_entry_intact_when_parent_chain_yields_nothing()
    {
        var folder = new Folder { Name = "F" }; // no defaults at all
        var entry = new ConnectionEntry
        {
            Name = "E", TeamViewerId = "123456789", ParentFolderId = folder.Id,
        };
        var folders = new Dictionary<Guid, Folder> { [folder.Id] = folder };

        var resolved = InheritanceResolver.Resolve(entry, folders);

        Assert.Null(resolved.Password);
        Assert.Null(resolved.Mode);
        Assert.Null(resolved.Quality);
        Assert.Null(resolved.AccessControl);
    }

    [Fact]
    public void Resolve_handles_cycle_in_folder_chain_without_infinite_loop()
    {
        // Corrupt data: folder A's parent = B, folder B's parent = A.
        var a = new Folder { Name = "A" };
        var b = new Folder { Name = "B", ParentFolderId = a.Id };
        a.ParentFolderId = b.Id;
        var entry = new ConnectionEntry
        {
            Name = "E", TeamViewerId = "123456789", ParentFolderId = a.Id,
        };
        var folders = new Dictionary<Guid, Folder> { [a.Id] = a, [b.Id] = b };

        // Should terminate, not hang.
        var resolved = InheritanceResolver.Resolve(entry, folders);
        Assert.NotNull(resolved);
    }

    [Fact]
    public void Resolve_handles_missing_folder_in_lookup_gracefully()
    {
        var entry = new ConnectionEntry
        {
            Name = "E", TeamViewerId = "123456789",
            ParentFolderId = Guid.NewGuid(), // points at a folder that doesn't exist in the lookup
        };
        var resolved = InheritanceResolver.Resolve(entry, new Dictionary<Guid, Folder>());
        Assert.Null(resolved.Password);
    }
}
