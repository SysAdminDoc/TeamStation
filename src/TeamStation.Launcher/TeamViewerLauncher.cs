using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using TeamStation.Core.Models;

namespace TeamStation.Launcher;

/// <summary>
/// Front-door launch API. Routes to TeamViewer.exe or the registered
/// TeamViewer protocol handler based on mode and user preference, returning
/// a <see cref="LaunchOutcome"/> rather than throwing for predictable caller
/// control flow.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TeamViewerLauncher
{
    private readonly Func<string?> _pathResolver;

    public TeamViewerLauncher() : this(TeamViewerPathResolver.Resolve) { }

    public TeamViewerLauncher(Func<string?> pathResolver)
    {
        _pathResolver = pathResolver;
    }

    public LaunchOutcome Launch(ConnectionEntry entry, LaunchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        options ??= LaunchOptions.Default;

        try
        {
            var plan = LaunchRoutePlanner.Plan(entry, options);
            return plan.Route == LaunchRoute.ProtocolHandler
                ? LaunchViaUri(entry)
                : LaunchViaCli(entry, options, passwordBytes: null, proxyPasswordBytes: null);
        }
        catch (Exception ex)
        {
            return LaunchOutcome.Failed(ex.Message);
        }
    }

    /// <summary>
    /// New in v0.3.4. Launch path that takes the password (and optionally
    /// the proxy password) as zeroable byte buffers instead of strings on
    /// <see cref="ConnectionEntry"/>. The buffers are zeroed via
    /// <see cref="CryptographicOperations.ZeroMemory(Span{byte})"/>
    /// immediately after argv has been composed and handed to
    /// <c>Process.Start</c>, so the cleartext lives in our address space
    /// for the absolute minimum window the launch flow allows.
    /// </summary>
    /// <remarks>
    /// Caller hands ownership of the byte arrays in. Even on failure paths
    /// the buffers are zeroed (try/finally), so no caller has to second-
    /// guess the lifecycle. URI-mode launches don't carry the password on
    /// the URI for non-control modes (see UriSchemeBuilder), so the
    /// proxyPasswordBytes parameter is silently ignored on those paths.
    /// </remarks>
    public LaunchOutcome Launch(
        ConnectionEntry entry,
        byte[]? passwordBytes,
        byte[]? proxyPasswordBytes,
        LaunchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        options ??= LaunchOptions.Default;

        try
        {
            try
            {
                var plan = LaunchRoutePlanner.Plan(entry, options);
                return plan.Route == LaunchRoute.ProtocolHandler
                    ? LaunchViaUri(entry)
                    : LaunchViaCli(entry, options, passwordBytes, proxyPasswordBytes);
            }
            catch (Exception ex)
            {
                return LaunchOutcome.Failed(ex.Message);
            }
        }
        finally
        {
            if (passwordBytes is { Length: > 0 })
                CryptographicOperations.ZeroMemory(passwordBytes);
            if (proxyPasswordBytes is { Length: > 0 })
                CryptographicOperations.ZeroMemory(proxyPasswordBytes);
        }
    }

    private LaunchOutcome LaunchViaCli(
        ConnectionEntry entry,
        LaunchOptions options,
        byte[]? passwordBytes,
        byte[]? proxyPasswordBytes)
    {
        var exe = string.IsNullOrWhiteSpace(entry.TeamViewerPathOverride)
            ? _pathResolver()
            : entry.TeamViewerPathOverride;
        if (string.IsNullOrWhiteSpace(exe))
            throw new FileNotFoundException(
            "TeamViewer.exe not found. Install TeamViewer and make sure the full client is available in its normal install location.");

        var argv = passwordBytes is null && proxyPasswordBytes is null
            ? CliArgvBuilder.Build(entry, base64Password: options.UseBase64Password)
            : CliArgvBuilder.Build(entry, passwordBytes, proxyPasswordBytes, base64Password: options.UseBase64Password);

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

public sealed record LaunchOptions(bool UseBase64Password, bool ForceUri, bool PreferProtocolHandler = false)
{
    public static readonly LaunchOptions Default = new(UseBase64Password: true, ForceUri: false, PreferProtocolHandler: false);
}

public sealed record LaunchOutcome(bool Success, int? ProcessId, string? ExePath, IReadOnlyList<string> Argv, string? Uri, string? Error)
{
    public static LaunchOutcome Started(int? pid, string? exe, IReadOnlyList<string> argv, string? uri) =>
        new(Success: true, pid, exe, argv, uri, Error: null);

    public static LaunchOutcome Failed(string error) =>
        new(Success: false, null, null, Array.Empty<string>(), null, error);
}
