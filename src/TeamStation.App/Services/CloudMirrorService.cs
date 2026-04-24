using System.IO;

namespace TeamStation.App.Services;

public static class CloudMirrorService
{
    public static void MirrorDatabase(string? sourceDatabasePath, string? destinationFolder)
    {
        if (string.IsNullOrWhiteSpace(sourceDatabasePath) ||
            string.IsNullOrWhiteSpace(destinationFolder) ||
            !File.Exists(sourceDatabasePath))
        {
            return;
        }

        Directory.CreateDirectory(destinationFolder);
        var destination = Path.Combine(destinationFolder, Path.GetFileName(sourceDatabasePath));
        File.Copy(sourceDatabasePath, destination, overwrite: true);

        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = sourceDatabasePath + suffix;
            if (File.Exists(sidecar))
                File.Copy(sidecar, destination + suffix, overwrite: true);
        }
    }
}
