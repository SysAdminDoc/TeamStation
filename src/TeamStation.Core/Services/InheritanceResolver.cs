using TeamStation.Core.Models;

namespace TeamStation.Core.Services;

/// <summary>
/// Walks a connection entry's folder chain to produce a fully-resolved copy
/// where <see cref="ConnectionEntry.Mode"/>, <see cref="ConnectionEntry.Quality"/>,
/// <see cref="ConnectionEntry.AccessControl"/>, and <see cref="ConnectionEntry.Password"/>
/// are populated from the nearest ancestor folder that carries a default.
/// </summary>
/// <remarks>
/// This is pure logic, invoked from the launch path. The result is a new
/// <see cref="ConnectionEntry"/> instance — the source entry is never mutated,
/// so "inherit" semantics survive across launches even if a folder's default changes.
/// </remarks>
public static class InheritanceResolver
{
    public static ConnectionEntry Resolve(ConnectionEntry entry, IReadOnlyDictionary<Guid, Folder> foldersById)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(foldersById);

        var mode = entry.Mode;
        var quality = entry.Quality;
        var ac = entry.AccessControl;
        var password = entry.Password;
        var tvPath = entry.TeamViewerPathOverride;
        var wakeBroadcast = entry.WakeBroadcastAddress;
        var preLaunchScript = entry.PreLaunchScript;
        var postLaunchScript = entry.PostLaunchScript;

        var anyNull =
            mode is null ||
            quality is null ||
            ac is null ||
            string.IsNullOrEmpty(password) ||
            string.IsNullOrEmpty(tvPath) ||
            string.IsNullOrEmpty(wakeBroadcast) ||
            string.IsNullOrEmpty(preLaunchScript) ||
            string.IsNullOrEmpty(postLaunchScript);
        if (anyNull)
        {
            foreach (var ancestor in WalkAncestors(entry.ParentFolderId, foldersById))
            {
                if (mode is null && ancestor.DefaultMode is { } m) mode = m;
                if (quality is null && ancestor.DefaultQuality is { } q) quality = q;
                if (ac is null && ancestor.DefaultAccessControl is { } a) ac = a;
                if (string.IsNullOrEmpty(password) && !string.IsNullOrEmpty(ancestor.DefaultPassword))
                    password = ancestor.DefaultPassword;
                if (string.IsNullOrEmpty(tvPath) && !string.IsNullOrEmpty(ancestor.DefaultTeamViewerPath))
                    tvPath = ancestor.DefaultTeamViewerPath;
                if (string.IsNullOrEmpty(wakeBroadcast) && !string.IsNullOrEmpty(ancestor.DefaultWakeBroadcastAddress))
                    wakeBroadcast = ancestor.DefaultWakeBroadcastAddress;
                if (string.IsNullOrEmpty(preLaunchScript) && !string.IsNullOrEmpty(ancestor.PreLaunchScript))
                    preLaunchScript = ancestor.PreLaunchScript;
                if (string.IsNullOrEmpty(postLaunchScript) && !string.IsNullOrEmpty(ancestor.PostLaunchScript))
                    postLaunchScript = ancestor.PostLaunchScript;

                if (mode is not null &&
                    quality is not null &&
                    ac is not null &&
                    !string.IsNullOrEmpty(password) &&
                    !string.IsNullOrEmpty(tvPath) &&
                    !string.IsNullOrEmpty(wakeBroadcast) &&
                    !string.IsNullOrEmpty(preLaunchScript) &&
                    !string.IsNullOrEmpty(postLaunchScript))
                {
                    break;
                }
            }
        }

        return new ConnectionEntry
        {
            Id = entry.Id,
            ParentFolderId = entry.ParentFolderId,
            Name = entry.Name,
            TeamViewerId = entry.TeamViewerId,
            ProfileName = entry.ProfileName,
            Password = password,
            Mode = mode,
            Quality = quality,
            AccessControl = ac,
            Proxy = entry.Proxy,
            TeamViewerPathOverride = tvPath,
            IsPinned = entry.IsPinned,
            WakeMacAddress = entry.WakeMacAddress,
            WakeBroadcastAddress = wakeBroadcast,
            PreLaunchScript = preLaunchScript,
            PostLaunchScript = postLaunchScript,
            Notes = entry.Notes,
            Tags = entry.Tags,
            LastConnectedUtc = entry.LastConnectedUtc,
            CreatedUtc = entry.CreatedUtc,
            ModifiedUtc = entry.ModifiedUtc,
        };
    }

    private static IEnumerable<Folder> WalkAncestors(Guid? startParentId, IReadOnlyDictionary<Guid, Folder> foldersById)
    {
        var cursor = startParentId;
        var visited = new HashSet<Guid>();
        while (cursor is { } id && visited.Add(id) && foldersById.TryGetValue(id, out var folder))
        {
            yield return folder;
            cursor = folder.ParentFolderId;
        }
    }
}
