using System.Globalization;
using System.Text;
using TeamStation.Core.Models;

namespace TeamStation.Core.Serialization;

/// <summary>
/// Imports a CSV file into TeamStation's folder + entry shape. The first row
/// is treated as a header and column names are matched case-insensitively
/// against a handful of common aliases, so the importer accepts exports from
/// TeamViewer's Management Console, Remote Desktop Manager, mRemoteNG, and
/// hand-rolled spreadsheets without upfront schema negotiation.
/// </summary>
/// <remarks>
/// Recognised column aliases:
/// <list type="bullet">
///   <item><c>name</c>, <c>alias</c>, <c>friendly_name</c>, <c>host</c>, <c>computer_name</c> → entry name</item>
///   <item><c>teamviewer_id</c>, <c>tv_id</c>, <c>remotecontrol_id</c>, <c>id</c>, <c>device_id</c> → TeamViewer ID</item>
///   <item><c>group</c>, <c>folder</c>, <c>category</c>, <c>parent</c> → folder (created if new)</item>
///   <item><c>password</c>, <c>pw</c> → password (plaintext in CSV — import re-encrypts)</item>
///   <item><c>notes</c>, <c>description</c>, <c>comment</c> → notes</item>
///   <item><c>tags</c>, <c>labels</c> → comma-separated tag list</item>
/// </list>
/// Rows whose TeamViewer ID is missing or non-numeric are skipped and
/// reported in <see cref="CsvImportResult.Skipped"/>.
/// </remarks>
public static class CsvImport
{
    private static readonly string[] NameAliases = { "name", "alias", "friendly_name", "friendlyname", "host", "computer_name", "computername" };
    private static readonly string[] IdAliases = { "teamviewer_id", "teamviewerid", "tv_id", "tvid", "remotecontrol_id", "remotecontrolid", "id", "device_id", "deviceid" };
    private static readonly string[] FolderAliases = { "group", "folder", "category", "parent" };
    private static readonly string[] PasswordAliases = { "password", "pw" };
    private static readonly string[] NotesAliases = { "notes", "description", "comment", "comments" };
    private static readonly string[] TagsAliases = { "tags", "labels" };

    public static CsvImportResult Parse(string csv, IReadOnlyList<Folder> existingFolders)
    {
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentNullException.ThrowIfNull(existingFolders);

        var rows = CsvLineReader.ReadAll(csv);
        var result = new CsvImportResult();

        if (rows.Count == 0) return result;

        var header = rows[0].Select(c => Normalize(c)).ToList();
        var idx = new ColumnIndex(header);

        if (idx.Id < 0)
        {
            result.Errors.Add("No TeamViewer ID column found (looked for: " + string.Join(", ", IdAliases) + ").");
            return result;
        }

        var foldersByName = existingFolders.ToDictionary(f => f.Name, f => f, StringComparer.OrdinalIgnoreCase);
        var newFolders = new Dictionary<string, Folder>(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace)) continue;

            var tvId = idx.Id < row.Count ? row[idx.Id].Trim() : string.Empty;
            if (!IsNumericId(tvId))
            {
                result.Skipped.Add((i + 1, $"non-numeric TeamViewer ID '{tvId}'"));
                continue;
            }

            var name = idx.Name >= 0 && idx.Name < row.Count ? row[idx.Name].Trim() : string.Empty;
            if (string.IsNullOrEmpty(name)) name = tvId;

            Guid? parentFolderId = null;
            if (idx.Folder >= 0 && idx.Folder < row.Count)
            {
                var folderName = row[idx.Folder].Trim();
                if (!string.IsNullOrEmpty(folderName))
                {
                    if (foldersByName.TryGetValue(folderName, out var existing))
                        parentFolderId = existing.Id;
                    else if (newFolders.TryGetValue(folderName, out var pending))
                        parentFolderId = pending.Id;
                    else
                    {
                        var created = new Folder { Name = folderName };
                        newFolders.Add(folderName, created);
                        parentFolderId = created.Id;
                    }
                }
            }

            var entry = new ConnectionEntry
            {
                Name = name,
                TeamViewerId = tvId,
                ParentFolderId = parentFolderId,
                Password = Value(row, idx.Password),
                Notes = Value(row, idx.Notes),
                Tags = ParseTags(Value(row, idx.Tags)),
            };
            result.Entries.Add(entry);
        }

        result.Folders.AddRange(newFolders.Values);
        return result;
    }

    private static string? Value(IReadOnlyList<string> row, int column)
    {
        if (column < 0 || column >= row.Count) return null;
        var v = row[column].Trim();
        return string.IsNullOrEmpty(v) ? null : v;
    }

    private static List<string> ParseTags(string? raw) =>
        string.IsNullOrEmpty(raw)
            ? new List<string>()
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static bool IsNumericId(string s) =>
        !string.IsNullOrEmpty(s) && s.Length is >= 8 and <= 12 && s.All(char.IsDigit);

    private static string Normalize(string header) =>
        new string((header ?? string.Empty).Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray()).ToLowerInvariant();

    private sealed class ColumnIndex
    {
        public int Name { get; }
        public int Id { get; }
        public int Folder { get; }
        public int Password { get; }
        public int Notes { get; }
        public int Tags { get; }

        public ColumnIndex(List<string> header)
        {
            Name = FindAny(header, NameAliases);
            Id = FindAny(header, IdAliases);
            Folder = FindAny(header, FolderAliases);
            Password = FindAny(header, PasswordAliases);
            Notes = FindAny(header, NotesAliases);
            Tags = FindAny(header, TagsAliases);
        }

        private static int FindAny(List<string> header, string[] aliases)
        {
            foreach (var alias in aliases)
            {
                var aliasNorm = new string(alias.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray()).ToLowerInvariant();
                var ix = header.IndexOf(aliasNorm);
                if (ix >= 0) return ix;
            }
            return -1;
        }
    }

    /// <summary>
    /// RFC 4180 line reader: handles quoted fields with embedded commas / newlines / escaped quotes.
    /// </summary>
    private static class CsvLineReader
    {
        public static List<List<string>> ReadAll(string csv)
        {
            var rows = new List<List<string>>();
            var field = new StringBuilder();
            var row = new List<string>();
            var inQuotes = false;

            for (var i = 0; i < csv.Length; i++)
            {
                var c = csv[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else field.Append(c);
                    continue;
                }

                switch (c)
                {
                    case '"': inQuotes = true; break;
                    case ',': row.Add(field.ToString()); field.Clear(); break;
                    case '\r':
                        if (i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                        goto case '\n';
                    case '\n':
                        row.Add(field.ToString()); field.Clear();
                        rows.Add(row); row = new List<string>();
                        break;
                    default: field.Append(c); break;
                }
            }

            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }

            return rows;
        }
    }
}

public sealed class CsvImportResult
{
    public List<Folder> Folders { get; } = new();
    public List<ConnectionEntry> Entries { get; } = new();
    public List<(int lineNumber, string reason)> Skipped { get; } = new();
    public List<string> Errors { get; } = new();
}
