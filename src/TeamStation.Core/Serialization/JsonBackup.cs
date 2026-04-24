using System.Text.Json;
using System.Text.Json.Serialization;
using TeamStation.Core.Models;

namespace TeamStation.Core.Serialization;

/// <summary>
/// JSON backup format for TeamStation's folder + entry graph. Includes a
/// format-version tag so we can evolve the shape without breaking old files.
/// Passwords are stored **in plaintext** on disk — callers must warn the user
/// before writing.
/// </summary>
public static class JsonBackup
{
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
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
        var dto = JsonSerializer.Deserialize<BackupDto>(json, Options)
                  ?? throw new InvalidDataException("Backup JSON was empty.");
        if (dto.FormatVersion < 1 || dto.FormatVersion > FormatVersion)
            throw new InvalidDataException($"Backup format version {dto.FormatVersion} not supported.");

        var folders = dto.Folders.Select(f => f.ToFolder()).ToList();
        var entries = dto.Entries.Select(e => e.ToEntry()).ToList();
        return (folders, entries);
    }

    private sealed class BackupDto
    {
        public int FormatVersion { get; set; }
        public DateTimeOffset ExportedAtUtc { get; set; }
        public List<FolderDto> Folders { get; set; } = new();
        public List<EntryDto> Entries { get; set; } = new();
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
        };
    }

    private sealed class EntryDto
    {
        public Guid Id { get; set; }
        public Guid? ParentFolderId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TeamViewerId { get; set; } = string.Empty;
        public string? Password { get; set; }
        public ConnectionMode? Mode { get; set; }
        public ConnectionQuality? Quality { get; set; }
        public AccessControl? AccessControl { get; set; }
        public ProxyDto? Proxy { get; set; }
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
            Password = e.Password,
            Mode = e.Mode,
            Quality = e.Quality,
            AccessControl = e.AccessControl,
            Proxy = e.Proxy is null ? null : ProxyDto.From(e.Proxy),
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
            Password = Password,
            Mode = Mode,
            Quality = Quality,
            AccessControl = AccessControl,
            Proxy = Proxy?.ToProxy(),
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
