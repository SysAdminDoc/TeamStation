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
}
