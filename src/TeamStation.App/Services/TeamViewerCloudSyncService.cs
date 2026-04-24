using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TeamStation.Core.Models;

namespace TeamStation.App.Services;

public sealed class TeamViewerCloudSyncService
{
    private readonly HttpClient _http;

    public TeamViewerCloudSyncService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { BaseAddress = new Uri("https://webapi.teamviewer.com/api/v1/") };
    }

    public async Task<CloudSyncResult> PullAsync(string apiToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiToken))
            throw new InvalidOperationException("TeamViewer API token is not configured.");

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        var groups = await GetArrayAsync("groups", "groups", cancellationToken).ConfigureAwait(false);
        var folders = new List<Folder>();
        var entries = new List<ConnectionEntry>();
        var root = new Folder { Id = StableId("teamviewer-cloud-root"), Name = "TV Cloud", AccentColor = "#89B4FA" };
        folders.Add(root);

        foreach (var group in groups)
        {
            var groupId = ReadString(group, "id", "groupid");
            var groupName = ReadString(group, "name") ?? "TeamViewer group";
            var folder = new Folder
            {
                Id = StableId($"teamviewer-cloud-group:{groupId ?? groupName}"),
                Name = groupName,
                ParentFolderId = root.Id,
                AccentColor = "#A6E3A1",
            };
            folders.Add(folder);

            if (string.IsNullOrWhiteSpace(groupId))
                continue;

            foreach (var device in await GetArrayAsync($"devices?groupid={Uri.EscapeDataString(groupId)}", "devices", cancellationToken).ConfigureAwait(false))
            {
                var id = ReadString(device, "remotecontrol_id", "teamviewer_id", "device_id", "id");
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                entries.Add(new ConnectionEntry
                {
                    Name = ReadString(device, "alias", "name", "description") ?? $"TeamViewer {id}",
                    TeamViewerId = new string(id.Where(char.IsDigit).ToArray()),
                    ProfileName = "TV Cloud",
                    ParentFolderId = folder.Id,
                    Mode = ConnectionMode.RemoteControl,
                    Tags = TagsFromDevice(device),
                    Notes = "Imported from TeamViewer Web API.",
                });
            }
        }

        return new CloudSyncResult(folders, entries);
    }

    private async Task<List<JsonElement>> GetArrayAsync(string path, string propertyName, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
            return document.RootElement.EnumerateArray().ToList();
        if (document.RootElement.TryGetProperty(propertyName, out var array) && array.ValueKind == JsonValueKind.Array)
            return array.EnumerateArray().ToList();
        return [];
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value))
                return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }

        return null;
    }

    private static List<string> TagsFromDevice(JsonElement device)
    {
        var tags = new List<string> { "tv-cloud" };
        if (ReadString(device, "online_state", "onlineState") is { Length: > 0 } state)
            tags.Add($"online={state}");
        if (ReadString(device, "groupid", "group_id") is { Length: > 0 } groupId)
            tags.Add($"group={groupId}");
        return tags;
    }

    private static Guid StableId(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var bytes = hash[..16];
        return new Guid(bytes);
    }
}

public sealed record CloudSyncResult(IReadOnlyList<Folder> Folders, IReadOnlyList<ConnectionEntry> Entries);
