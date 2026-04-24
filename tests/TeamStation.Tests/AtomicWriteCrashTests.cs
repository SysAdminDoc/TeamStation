using TeamStation.App.Services;

namespace TeamStation.Tests;

/// <summary>
/// Simulates a crash in the middle of <see cref="SettingsService.Save"/>'s
/// write-then-rename sequence and asserts the two invariants that keep
/// the file store safe:
/// <list type="number">
///   <item>The original target file is <b>not</b> mutated when the rename fails.</item>
///   <item>The per-save <c>.tmp</c> sidecar is always cleaned up — no residue
///   in the settings directory, even after repeated failed saves.</item>
/// </list>
/// The <c>.tmp</c> name embeds a per-save Guid, so a stale sidecar from a
/// previous failure would otherwise grow without bound.
/// </summary>
public class AtomicWriteCrashTests
{
    [Fact]
    public void SettingsService_Save_leaves_original_intact_when_rename_target_is_a_directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ts-atomic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        // Force the rename step to fail: the "target file" is actually a
        // directory on disk, so File.Move(..., overwrite: true) can never
        // succeed. This is the cleanest deterministic simulation of a
        // mid-rename crash that leaves the temp file orphaned.
        var targetAsDir = Path.Combine(dir, "settings.json");
        Directory.CreateDirectory(targetAsDir);

        try
        {
            var svc = new SettingsService(targetAsDir);

            Assert.ThrowsAny<Exception>(() =>
                svc.Save(new AppSettings { HistoryRetentionDays = 99 }));

            // Original path still resolves to the directory we seeded,
            // byte-for-byte unchanged.
            Assert.True(Directory.Exists(targetAsDir));
            Assert.False(File.Exists(targetAsDir));

            // No leftover *.tmp files anywhere under the settings dir.
            var residue = Directory.EnumerateFiles(dir, "*.tmp", SearchOption.AllDirectories).ToArray();
            Assert.Empty(residue);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SettingsService_Save_cleans_up_temp_even_across_repeated_failures()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ts-atomic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var targetAsDir = Path.Combine(dir, "settings.json");
        Directory.CreateDirectory(targetAsDir);

        try
        {
            var svc = new SettingsService(targetAsDir);

            for (var i = 0; i < 5; i++)
            {
                Assert.ThrowsAny<Exception>(() =>
                    svc.Save(new AppSettings { HistoryRetentionDays = 42 + i }));
            }

            // Five failed saves must not leave five temp files lying around.
            var residue = Directory.EnumerateFiles(dir, "*.tmp", SearchOption.AllDirectories).ToArray();
            Assert.Empty(residue);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SettingsService_Save_roundtrip_still_works_after_a_failed_save()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ts-atomic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "settings.json");

        try
        {
            var svc = new SettingsService(target);

            // Happy path: save works.
            svc.Save(new AppSettings { HistoryRetentionDays = 7 });
            var first = svc.Load();
            Assert.Equal(7, first.HistoryRetentionDays);

            // Now swap the target into a directory, trip the crash, then
            // restore the file and confirm the next save still works.
            File.Delete(target);
            Directory.CreateDirectory(target);
            Assert.ThrowsAny<Exception>(() =>
                svc.Save(new AppSettings { HistoryRetentionDays = 99 }));
            Directory.Delete(target);

            // New save lands cleanly.
            svc.Save(new AppSettings { HistoryRetentionDays = 31 });
            var second = svc.Load();
            Assert.Equal(31, second.HistoryRetentionDays);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
