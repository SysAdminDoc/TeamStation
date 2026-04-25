using System.Text.Json;
using System.Text.Json.Serialization;
using TeamStation.Core.Models;

namespace TeamStation.Core.Serialization;

/// <summary>
/// JSON backup format for TeamStation's folder + entry graph. Includes a
/// format-version tag so the shape can evolve without breaking old files.
/// Passwords are stored <b>in plaintext</b> on disk — callers must warn the
/// user before writing.
/// </summary>
/// <remarks>
/// <para>
/// Format-version history:
/// <list type="bullet">
///   <item><c>1</c> — v0.1.x. Core folder/entry fields, proxy, tags, timestamps.</item>
///   <item><c>2</c> — v0.2.1. Adds every persisted field: profile, pin state,
///     TeamViewer.exe override, Wake-on-LAN, launch scripts on both entries
///     and folders. v1 files still parse; missing fields read as null/false.</item>
/// </list>
/// </para>
/// </remarks>
public static class JsonBackup
{
    public const int FormatVersion = 2;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Build(IEnumerable<Folder> folders, IEnumerable<ConnectionEntry> entries)
    {
        var dto = new BackupDto
        {
            FormatVersion = FormatVersion,
            ExportedAtUtc = DateTimeOffset.UtcNow,
            Folders = folders.Select(FolderDto.From).ToList(),
            Entries = entries.Select(EntryDto.From).ToList(),
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    public static (List<Folder> folders, List<ConnectionEntry> entries) Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("Backup JSON was empty.");

        BackupDto? dto;
        try { dto = JsonSerializer.Deserialize<BackupDto>(json, Options); }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Backup JSON is malformed: {ex.Message}", ex);
        }
        if (dto is null)
            throw new InvalidDataException("Backup JSON was empty.");
        if (dto.FormatVersion < 1)
            throw new InvalidDataException(
                $"Backup has no format version (got {dto.FormatVersion}). This file may be from a different tool.");
        if (dto.FormatVersion > FormatVersion)
            throw new InvalidDataException(
                $"Backup format version {dto.FormatVersion} is newer than this build supports (max {FormatVersion}). Upgrade TeamStation and retry.");

        var folderDtos = dto.Folders ?? new();
        var entryDtos = dto.Entries ?? new();
        EnsureUniqueIds(folderDtos, static folder => folder.Id, static (folder, id) => folder.Id = id);
        EnsureUniqueIds(entryDtos, static entry => entry.Id, static (entry, id) => entry.Id = id);
        ValidateEntries(entryDtos);

        var folders = folderDtos.Select(f => f.ToFolder()).ToList();
        var entries = entryDtos.Select(e => e.ToEntry()).ToList();

        // Null-out parent references that don't resolve within the import set.
        // Without this, inserting an entry whose ParentFolderId points at a folder
        // not in the import (and not already in the DB) would hit the foreign-key
        // constraint. Defensive: re-home orphans to root rather than aborting.
        var folderIds = new HashSet<Guid>(folders.Select(f => f.Id));
        foreach (var f in folders)
            if (f.ParentFolderId is { } pid && !folderIds.Contains(pid))
                f.ParentFolderId = null;
        foreach (var e in entries)
            if (e.ParentFolderId is { } pid && !folderIds.Contains(pid))
                e.ParentFolderId = null;

        return (folders, entries);
    }

    private static void EnsureUniqueIds<T>(IEnumerable<T> items, Func<T, Guid> getId, Action<T, Guid> setId)
    {
        var seen = new HashSet<Guid>();
        foreach (var item in items)
        {
            var id = getId(item);
            if (id != Guid.Empty && seen.Add(id))
                continue;

            Guid replacement;
            do
            {
                replacement = Guid.NewGuid();
            } while (!seen.Add(replacement));

            setId(item, replacement);
        }
    }

    private static void ValidateEntries(IEnumerable<EntryDto> entries)
    {
        foreach (var entry in entries)
        {
            if (TeamViewerIdFormat.IsValid(entry.TeamViewerId))
                continue;

            var label = string.IsNullOrWhiteSpace(entry.Name) ? entry.Id.ToString("D") : entry.Name;
            throw new InvalidDataException(
                $"Backup entry \"{label}\" has an invalid TeamViewer ID. IDs must be 8-12 ASCII digits.");
        }
    }

    private sealed class BackupDto
    {
        public int FormatVersion { get; set; }
        public DateTimeOffset ExportedAtUtc { get; set; }
        public List<FolderDto>? Folders { get; set; }
        public List<EntryDto>? Entries { get; set; }
    }

    private sealed class FolderDto
    {
        public Guid Id { get; set; }
        public Guid? ParentFolderId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? AccentColor { get; set; }
        public int SortOrder { get; set; }
        public ConnectionMode? DefaultMode { get; set; }
        public ConnectionQuality? DefaultQuality { get; set; }
        public AccessControl? DefaultAccessControl { get; set; }
        public string? DefaultPassword { get; set; }
        public string? DefaultTeamViewerPath { get; set; }
        public string? DefaultWakeBroadcastAddress { get; set; }
        public string? PreLaunchScript { get; set; }
        public string? PostLaunchScript { get; set; }

        public static FolderDto From(Folder f) => new()
        {
            Id = f.Id,
            ParentFolderId = f.ParentFolderId,
            Name = f.Name,
            AccentColor = f.AccentColor,
            SortOrder = f.SortOrder,
            DefaultMode = f.DefaultMode,
            DefaultQuality = f.DefaultQuality,
            DefaultAccessControl = f.DefaultAccessControl,
            DefaultPassword = f.DefaultPassword,
            DefaultTeamViewerPath = f.DefaultTeamViewerPath,
            DefaultWakeBroadcastAddress = f.DefaultWakeBroadcastAddress,
            PreLaunchScript = f.PreLaunchScript,
            PostLaunchScript = f.PostLaunchScript,
        };

        public Folder ToFolder() => new()
        {
            Id = Id,
            ParentFolderId = ParentFolderId,
            Name = Name,
            AccentColor = AccentColor,
            SortOrder = SortOrder,
            DefaultMode = DefaultMode,
            DefaultQuality = DefaultQuality,
            DefaultAccessControl = DefaultAccessControl,
            DefaultPassword = DefaultPassword,
            DefaultTeamViewerPath = DefaultTeamViewerPath,
            DefaultWakeBroadcastAddress = DefaultWakeBroadcastAddress,
            PreLaunchScript = PreLaunchScript,
            PostLaunchScript = PostLaunchScript,
        };
    }

    private sealed class EntryDto
    {
        public Guid Id { get; set; }
        public Guid? ParentFolderId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TeamViewerId { get; set; } = string.Empty;
        public string ProfileName { get; set; } = "Default";
        public string? Password { get; set; }
        public ConnectionMode? Mode { get; set; }
        public ConnectionQuality? Quality { get; set; }
        public AccessControl? AccessControl { get; set; }
        public ProxyDto? Proxy { get; set; }
        public string? TeamViewerPathOverride { get; set; }
        public bool IsPinned { get; set; }
        public string? WakeMacAddress { get; set; }
        public string? WakeBroadcastAddress { get; set; }
        public string? PreLaunchScript { get; set; }
        public string? PostLaunchScript { get; set; }
        public string? Notes { get; set; }
        public List<string> Tags { get; set; } = new();
        public DateTimeOffset? LastConnectedUtc { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }
        public DateTimeOffset ModifiedUtc { get; set; }

        public static EntryDto From(ConnectionEntry e) => new()
        {
            Id = e.Id,
            ParentFolderId = e.ParentFolderId,
            Name = e.Name,
            TeamViewerId = e.TeamViewerId,
            ProfileName = string.IsNullOrWhiteSpace(e.ProfileName) ? "Default" : e.ProfileName,
            Password = e.Password,
            Mode = e.Mode,
            Quality = e.Quality,
            AccessControl = e.AccessControl,
            Proxy = e.Proxy is null ? null : ProxyDto.From(e.Proxy),
            TeamViewerPathOverride = e.TeamViewerPathOverride,
            IsPinned = e.IsPinned,
            WakeMacAddress = e.WakeMacAddress,
            WakeBroadcastAddress = e.WakeBroadcastAddress,
            PreLaunchScript = e.PreLaunchScript,
            PostLaunchScript = e.PostLaunchScript,
            Notes = e.Notes,
            Tags = e.Tags,
            LastConnectedUtc = e.LastConnectedUtc,
            CreatedUtc = e.CreatedUtc,
            ModifiedUtc = e.ModifiedUtc,
        };

        public ConnectionEntry ToEntry() => new()
        {
            Id = Id,
            ParentFolderId = ParentFolderId,
            Name = Name,
            TeamViewerId = TeamViewerId,
            ProfileName = string.IsNullOrWhiteSpace(ProfileName) ? "Default" : ProfileName,
            Password = Password,
            Mode = Mode,
            Quality = Quality,
            AccessControl = AccessControl,
            Proxy = Proxy?.ToProxy(),
            TeamViewerPathOverride = TeamViewerPathOverride,
            IsPinned = IsPinned,
            WakeMacAddress = WakeMacAddress,
            WakeBroadcastAddress = WakeBroadcastAddress,
            PreLaunchScript = PreLaunchScript,
            PostLaunchScript = PostLaunchScript,
            Notes = Notes,
            Tags = Tags,
            LastConnectedUtc = LastConnectedUtc,
            CreatedUtc = CreatedUtc,
            ModifiedUtc = ModifiedUtc,
        };
    }

    private sealed class ProxyDto
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }

        public static ProxyDto From(ProxySettings p) => new()
        {
            Host = p.Host,
            Port = p.Port,
            Username = p.Username,
            Password = p.Password,
        };

        public ProxySettings ToProxy() => new(Host, Port, Username, Password);
    }
}
