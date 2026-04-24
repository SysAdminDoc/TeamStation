using System.Diagnostics;
using System.Runtime.Versioning;
using TeamStation.Core.Models;

namespace TeamStation.Launcher;

/// <summary>
/// Front-door launch API. Routes to CLI or URI handler based on mode,
/// returning a <see cref="LaunchOutcome"/> rather than throwing for
/// predictable caller control flow.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TeamViewerLauncher
{
    private readonly Func<string?> _pathResolver;

    public TeamViewerLauncher() : this(TeamViewerPathResolver.Resolve) { }

    internal TeamViewerLauncher(Func<string?> pathResolver)
    {
        _pathResolver = pathResolver;
    }

    public LaunchOutcome Launch(ConnectionEntry entry, LaunchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        options ??= LaunchOptions.Default;

        try
        {
            return options.ForceUri || UriSchemeBuilder.IsUriOnly(entry.Mode)
                ? LaunchViaUri(entry)
                : LaunchViaCli(entry, options);
        }
        catch (Exception ex)
        {
            return LaunchOutcome.Failed(ex.Message);
        }
    }

    private LaunchOutcome LaunchViaCli(ConnectionEntry entry, LaunchOptions options)
    {
        var exe = _pathResolver() ?? throw new FileNotFoundException(
            "TeamViewer.exe not found. Install TeamViewer or set the path manually in Settings.");

        var argv = CliArgvBuilder.Build(entry, base64Password: options.UseBase64Password);
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        foreach (var a in argv) psi.ArgumentList.Add(a);

        var process = Process.Start(psi) ??
            throw new InvalidOperationException("TeamViewer process failed to start.");
        return LaunchOutcome.Started(process.Id, exe, argv, null);
    }

    private static LaunchOutcome LaunchViaUri(ConnectionEntry entry)
    {
        var uri = UriSchemeBuilder.Build(entry);
        var psi = new ProcessStartInfo
        {
            FileName = uri,
            UseShellExecute = true,
        };
        var process = Process.Start(psi);
        return LaunchOutcome.Started(process?.Id, null, Array.Empty<string>(), uri);
    }
}

public sealed record LaunchOptions(bool UseBase64Password, bool ForceUri)
{
    public static readonly LaunchOptions Default = new(UseBase64Password: true, ForceUri: false);
}

public sealed record LaunchOutcome(bool Success, int? ProcessId, string? ExePath, IReadOnlyList<string> Argv, string? Uri, string? Error)
{
    public static LaunchOutcome Started(int? pid, string? exe, IReadOnlyList<string> argv, string? uri) =>
        new(Success: true, pid, exe, argv, uri, Error: null);

    public static LaunchOutcome Failed(string error) =>
        new(Success: false, null, null, Array.Empty<string>(), null, error);
}
