using System.Text;
using TeamStation.Core.Models;
using TeamStation.Launcher.Validation;

namespace TeamStation.Launcher;

/// <summary>
/// Builds the <c>TeamViewer.exe</c> argv array for a connection. All values are
/// validated against <see cref="LaunchInputValidator"/> before being added so
/// no string reaches a shell unsanitized.
/// </summary>
/// <remarks>
/// The CLI <c>--mode</c> flag only supports <c>fileTransfer</c> and <c>vpn</c>.
/// For Chat, VideoCall, and Presentation modes callers must route through
/// <see cref="UriSchemeBuilder"/> instead — <see cref="Build"/> throws for them.
/// </remarks>
public static class CliArgvBuilder
{
    /// <summary>
    /// New in v0.3.4. Build argv from explicit byte[] password buffers
    /// supplied by a credential-aware caller. When non-null, the byte[]
    /// arrays override <see cref="ConnectionEntry.Password"/> and
    /// <see cref="ProxySettings.Password"/> respectively. The byte[] arrays
    /// are NOT zeroed inside this method — caller (e.g.
    /// <see cref="TeamViewerLauncher"/>) owns lifecycle and is expected to
    /// zero immediately after argv has been handed to <c>Process.Start</c>.
    /// </summary>
    public static IReadOnlyList<string> Build(
        ConnectionEntry entry,
        byte[]? passwordBytes,
        byte[]? proxyPasswordBytes,
        bool base64Password = true)
    {
        ArgumentNullException.ThrowIfNull(entry);
        LaunchInputValidator.ValidateTeamViewerId(entry.TeamViewerId);

        var argv = new List<string>(16) { "--id", entry.TeamViewerId };

        if (passwordBytes is { Length: > 0 })
        {
            // Validation reuses the existing string-based path. The transient
            // string is method-local and will be GC'd; what made the
            // System.String credential leak meaningful was long-lived field
            // references (settings.TeamViewerApiToken / entry.Password), not
            // microsecond-scope locals.
            var pw = Encoding.UTF8.GetString(passwordBytes);
            LaunchInputValidator.ValidatePassword(pw);
            if (base64Password)
            {
                argv.Add("--PasswordB64");
                argv.Add(Convert.ToBase64String(passwordBytes));
            }
            else
            {
                argv.Add("--Password");
                argv.Add(pw);
            }
        }
        else if (!string.IsNullOrEmpty(entry.Password))
        {
            LaunchInputValidator.ValidatePassword(entry.Password);
            if (base64Password)
            {
                var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.Password));
                argv.Add("--PasswordB64");
                argv.Add(b64);
            }
            else
            {
                argv.Add("--Password");
                argv.Add(entry.Password);
            }
        }

        AppendModeQualityAccessAndProxy(argv, entry, proxyPasswordBytes);
        return argv;
    }

    public static IReadOnlyList<string> Build(ConnectionEntry entry, bool base64Password = true)
    {
        ArgumentNullException.ThrowIfNull(entry);
        LaunchInputValidator.ValidateTeamViewerId(entry.TeamViewerId);

        var argv = new List<string>(16) { "--id", entry.TeamViewerId };

        if (!string.IsNullOrEmpty(entry.Password))
        {
            LaunchInputValidator.ValidatePassword(entry.Password);
            if (base64Password)
            {
                var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.Password));
                argv.Add("--PasswordB64");
                argv.Add(b64);
            }
            else
            {
                argv.Add("--Password");
                argv.Add(entry.Password);
            }
        }

        AppendModeQualityAccessAndProxy(argv, entry, proxyPasswordBytes: null);
        return argv;
    }

    private static void AppendModeQualityAccessAndProxy(
        List<string> argv,
        ConnectionEntry entry,
        byte[]? proxyPasswordBytes)
    {
        switch (entry.Mode)
        {
            case ConnectionMode.FileTransfer:
                argv.Add("--mode"); argv.Add("fileTransfer"); break;
            case ConnectionMode.Vpn:
                argv.Add("--mode"); argv.Add("vpn"); break;
            case null:
            case ConnectionMode.RemoteControl:
                // null or default mode — no flag
                break;
            case ConnectionMode.Chat:
            case ConnectionMode.VideoCall:
            case ConnectionMode.Presentation:
                throw new InvalidOperationException(
                    $"Mode {entry.Mode} is not supported by the TeamViewer CLI; use UriSchemeBuilder instead.");
        }

        // Enum.IsDefined guards against malformed DB rows that carry an
        // integer outside the enum range (e.g. a v1 row with `quality=55`
        // forcibly cast through the CLR). Emitting the raw int would let
        // TeamViewer reject at its own layer, but the guard belongs here:
        // it keeps the argv clean and the log panel noise-free.
        if (entry.Quality is { } q && q != ConnectionQuality.AutoSelect && Enum.IsDefined(q))
        {
            argv.Add("--quality");
            argv.Add(((int)q).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (entry.AccessControl is { } ac && ac != AccessControl.Undefined && Enum.IsDefined(ac))
        {
            argv.Add("--ac");
            argv.Add(((int)ac).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (entry.Proxy is { } proxy)
        {
            LaunchInputValidator.ValidateProxyEndpoint(proxy.Endpoint);
            argv.Add("--ProxyIP");
            argv.Add(proxy.Endpoint);

            if (!string.IsNullOrEmpty(proxy.Username))
            {
                LaunchInputValidator.ValidateProxyUsername(proxy.Username);
                argv.Add("--ProxyUser");
                argv.Add(proxy.Username);
            }

            if (proxyPasswordBytes is { Length: > 0 })
            {
                var pw = Encoding.UTF8.GetString(proxyPasswordBytes);
                LaunchInputValidator.ValidatePassword(pw);
                argv.Add("--ProxyPassword");
                argv.Add(Convert.ToBase64String(proxyPasswordBytes));
            }
            else if (!string.IsNullOrEmpty(proxy.Password))
            {
                LaunchInputValidator.ValidatePassword(proxy.Password);
                var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(proxy.Password));
                argv.Add("--ProxyPassword");
                argv.Add(b64);
            }
        }
    }
}
