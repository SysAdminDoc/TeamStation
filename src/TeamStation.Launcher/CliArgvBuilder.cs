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

        switch (entry.Mode)
        {
            case ConnectionMode.FileTransfer:
                argv.Add("--mode"); argv.Add("fileTransfer"); break;
            case ConnectionMode.Vpn:
                argv.Add("--mode"); argv.Add("vpn"); break;
            case ConnectionMode.RemoteControl:
                // default mode — no flag
                break;
            case ConnectionMode.Chat:
            case ConnectionMode.VideoCall:
            case ConnectionMode.Presentation:
                throw new InvalidOperationException(
                    $"Mode {entry.Mode} is not supported by the TeamViewer CLI; use UriSchemeBuilder instead.");
        }

        if (entry.Quality != ConnectionQuality.AutoSelect)
        {
            argv.Add("--quality");
            argv.Add(((int)entry.Quality).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (entry.AccessControl != AccessControl.Undefined)
        {
            argv.Add("--ac");
            argv.Add(((int)entry.AccessControl).ToString(System.Globalization.CultureInfo.InvariantCulture));
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

            if (!string.IsNullOrEmpty(proxy.Password))
            {
                LaunchInputValidator.ValidatePassword(proxy.Password);
                var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(proxy.Password));
                argv.Add("--ProxyPassword");
                argv.Add(b64);
            }
        }

        return argv;
    }
}
