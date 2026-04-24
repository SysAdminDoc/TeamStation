using TeamStation.Core.Io;

namespace TeamStation.Tests;

/// <summary>
/// Twin of <see cref="AtomicWriteCrashTests"/> but targeting
/// <see cref="AtomicFile.WriteAllText(string,string)"/> directly. Both the
/// settings store and the JSON-backup export path route through this helper
/// in v0.3.1, so pinning the helper pins the contract for both call sites.
/// </summary>
public class AtomicFileTests
{
    [Fact]
    public void WriteAllText_leaves_original_intact_when_rename_target_is_a_directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ts-af-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        // Turn the "target file" into a directory so File.Move(..., overwrite:
        // true) can never succeed. This is the cleanest deterministic
        // simulation of a mid-rename crash — it exercises the catch-block
        // cleanup without any IO races.
        var targetAsDir = Path.Combine(dir, "backup.json");
        Directory.CreateDirectory(targetAsDir);

        try
        {
            Assert.ThrowsAny<Exception>(() =>
                AtomicFile.WriteAllText(targetAsDir, "{\"hello\":\"world\"}"));

            // The target is still the directory we seeded — AtomicFile did
            // not replace it with the file we attempted to write.
            Assert.True(Directory.Exists(targetAsDir));
            Assert.False(File.Exists(targetAsDir));

            // No residue .tmp sidecar anywhere under the test directory.
            var residue = Directory.EnumerateFiles(dir, "*.tmp", SearchOption.AllDirectories).ToArray();
            Assert.Empty(residue);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void WriteAllText_cleans_up_temp_even_across_repeated_failures()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ts-af-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var targetAsDir = Path.Combine(dir, "backup.json");
        Directory.CreateDirectory(targetAsDir);

        try
        {
            for (var i = 0; i < 5; i++)
            {
                Assert.ThrowsAny<Exception>(() =>
                    AtomicFile.WriteAllText(targetAsDir, $"attempt-{i}"));
            }

            // Five failed saves must not leave five temp files lying around.
            // The per-save GUID sidecar name would otherwise grow unbounded.
            var residue = Directory.EnumerateFiles(dir, "*.tmp", SearchOption.AllDirectories).ToArray();
            Assert.Empty(residue);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void WriteAllText_roundtrip_still_works_after_a_failed_save()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ts-af-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "backup.json");

        try
        {
            AtomicFile.WriteAllText(target, "v1");
            Assert.Equal("v1", File.ReadAllText(target));

            // Swap the target into a directory, trip the crash, restore,
            // then confirm the next save still works. Pins the invariant
            // that nothing about the helper's internal state leaks across
            // invocations (e.g. a cached temp path name).
            File.Delete(target);
            Directory.CreateDirectory(target);
            Assert.ThrowsAny<Exception>(() => AtomicFile.WriteAllText(target, "v2"));
            Directory.Delete(target);

            AtomicFile.WriteAllText(target, "v3");
            Assert.Equal("v3", File.ReadAllText(target));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void WriteAllText_happy_path_creates_target_directory_if_missing()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ts-af-{Guid.NewGuid():N}", "sub", "dir");
        var target = Path.Combine(dir, "backup.json");

        try
        {
            // Directory does not exist yet — helper must create it rather
            // than dropping the temp file alongside and failing on the move.
            AtomicFile.WriteAllText(target, "created");
            Assert.True(File.Exists(target));
            Assert.Equal("created", File.ReadAllText(target));
        }
        finally
        {
            try
            {
                var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(target))!);
                if (root is not null && Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch { }
        }
    }

    [Fact]
    public void WriteAllBytes_leaves_original_intact_when_rename_target_is_a_directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ts-af-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var targetAsDir = Path.Combine(dir, "blob.bin");
        Directory.CreateDirectory(targetAsDir);

        try
        {
            Assert.ThrowsAny<Exception>(() =>
                AtomicFile.WriteAllBytes(targetAsDir, new byte[] { 0x41, 0x42, 0x43 }));

            Assert.True(Directory.Exists(targetAsDir));
            var residue = Directory.EnumerateFiles(dir, "*.tmp", SearchOption.AllDirectories).ToArray();
            Assert.Empty(residue);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
