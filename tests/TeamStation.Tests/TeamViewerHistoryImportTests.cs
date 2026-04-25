using TeamStation.Core.Models;
using TeamStation.Core.Serialization;

namespace TeamStation.Tests;

public class TeamViewerHistoryImportTests
{
    [Fact]
    public void ParseFiles_imports_unique_ids_and_guesses_names()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ts-history-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path,
        [
            "Alice Workstation, 123456789",
            "Duplicate 123456789",
            "Server Rack | 987654321",
            "too short 1234567",
        ]);

        try
        {
            var imported = TeamViewerHistoryImport.ParseFiles([path], []);

            Assert.Equal(2, imported.Count);
            Assert.Equal("Alice Workstation", imported[0].Name);
            Assert.Equal("123456789", imported[0].TeamViewerId);
            Assert.Equal("Server Rack", imported[1].Name);
            Assert.Equal("987654321", imported[1].TeamViewerId);
            Assert.All(imported, entry => Assert.Contains("teamviewer-history", entry.Tags));
        }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void ParseFiles_ignores_unicode_decimal_digits()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ts-history-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path,
        [
            "Valid 123456789",
            "Looks numeric \u0661\u0662\u0663\u0664\u0665\u0666\u0667\u0668\u0669",
        ]);

        try
        {
            var imported = TeamViewerHistoryImport.ParseFiles([path], []);

            var entry = Assert.Single(imported);
            Assert.Equal("123456789", entry.TeamViewerId);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void ParseFiles_skips_existing_ids()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ts-history-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "Known 123456789");

        try
        {
            var existing = new[] { new ConnectionEntry { Name = "Known", TeamViewerId = "123456789" } };
            var imported = TeamViewerHistoryImport.ParseFiles([path], existing);

            Assert.Empty(imported);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void ScanFiles_reports_read_and_missing_sources()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ts-history-{Guid.NewGuid():N}.txt");
        var missingPath = Path.Combine(Path.GetTempPath(), $"ts-history-missing-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "Ops jump host 123456789");

        try
        {
            var result = TeamViewerHistoryImport.ScanFiles([path, missingPath], []);

            Assert.Single(result.Entries);
            Assert.Contains(path, result.ReadPaths);
            Assert.Contains(missingPath, result.MissingPaths);
            Assert.Empty(result.ReadErrors);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void ScanFiles_trims_and_deduplicates_source_paths()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ts-history-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "Ops jump host 123456789");

        try
        {
            var result = TeamViewerHistoryImport.ScanFiles([$"  {path}  ", path], []);

            Assert.Single(result.Entries);
            Assert.Equal([path], result.ReadPaths);
            Assert.Empty(result.MissingPaths);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void ParseFiles_sanitizes_control_characters_and_caps_imported_names()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ts-history-{Guid.NewGuid():N}.txt");
        var noisyName = "Front\tDesk\u0001" + new string('x', 200);
        File.WriteAllText(path, $"{noisyName} 123456789");

        try
        {
            var entry = Assert.Single(TeamViewerHistoryImport.ParseFiles([path], []));

            Assert.DoesNotContain(entry.Name, char.IsControl);
            Assert.DoesNotContain('\t', entry.Name);
            Assert.True(entry.Name.Length <= 96);
            Assert.StartsWith("Front Desk", entry.Name, StringComparison.Ordinal);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void ParseFiles_uses_clear_fallback_name_when_history_line_has_no_label()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ts-history-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "--- 123456789 :::");

        try
        {
            var entry = Assert.Single(TeamViewerHistoryImport.ParseFiles([path], []));

            Assert.Equal("TeamViewer 123456789", entry.Name);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort */ }
        }
    }
}
