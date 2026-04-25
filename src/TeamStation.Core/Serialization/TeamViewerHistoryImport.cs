using System.Text.RegularExpressions;
using TeamStation.Core.Models;

namespace TeamStation.Core.Serialization;

public sealed record TeamViewerHistoryImportResult(
    IReadOnlyList<ConnectionEntry> Entries,
    IReadOnlyList<string> ReadPaths,
    IReadOnlyList<string> MissingPaths,
    IReadOnlyList<string> ReadErrors);

public static partial class TeamViewerHistoryImport
{
    public static IReadOnlyList<ConnectionEntry> ParseFiles(IEnumerable<string> paths, IEnumerable<ConnectionEntry> existing) =>
        ScanFiles(paths, existing).Entries;

    public static TeamViewerHistoryImportResult ScanFiles(IEnumerable<string> paths, IEnumerable<ConnectionEntry> existing)
    {
        var knownIds = existing.Select(e => e.TeamViewerId).ToHashSet(StringComparer.Ordinal);
        var created = new List<ConnectionEntry>();
        var readPaths = new List<string>();
        var missingPaths = new List<string>();
        var readErrors = new List<string>();

        foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            if (!File.Exists(path))
            {
                missingPaths.Add(path);
                continue;
            }

            try
            {
                foreach (var line in File.ReadLines(path))
                {
                    var match = TeamViewerIdRegex().Match(line);
                    if (!match.Success || knownIds.Contains(match.Value))
                        continue;

                    knownIds.Add(match.Value);
                    created.Add(new ConnectionEntry
                    {
                        Name = GuessName(line, match.Value),
                        TeamViewerId = match.Value,
                        ProfileName = "Imported history",
                        Mode = ConnectionMode.RemoteControl,
                        Quality = ConnectionQuality.AutoSelect,
                        AccessControl = AccessControl.Undefined,
                        Tags = ["teamviewer-history"],
                        Notes = $"Imported from {Path.GetFileName(path)}.",
                    });
                }

                readPaths.Add(path);
            }
            catch (Exception ex)
            {
                readErrors.Add($"{path}: {ex.Message}");
            }
        }

        return new TeamViewerHistoryImportResult(created, readPaths, missingPaths, readErrors);
    }

    public static IReadOnlyList<string> DefaultPaths()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "TeamViewer");
        return
        [
            Path.Combine(dir, "Connections.txt"),
            Path.Combine(dir, "Connections_incoming.txt"),
        ];
    }

    private static string GuessName(string line, string id)
    {
        var cleaned = line.Replace(id, string.Empty, StringComparison.Ordinal)
            .Trim(' ', '\t', ',', ';', '|', '-', ':', '"');
        return string.IsNullOrWhiteSpace(cleaned) ? $"TeamViewer {id}" : cleaned;
    }

    [GeneratedRegex("\\b\\d{8,12}\\b")]
    private static partial Regex TeamViewerIdRegex();
}
