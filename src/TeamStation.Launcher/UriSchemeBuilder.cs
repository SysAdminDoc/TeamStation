using TeamStation.Core.Models;
using TeamStation.Launcher.Validation;

namespace TeamStation.Launcher;

/// <summary>
/// Constructs the TeamViewer URI-handler URL for a connection. URI launch is
/// the only supported path for Chat, VideoCall, and Presentation modes, and
/// acts as a fallback for the other modes.
/// </summary>
/// <remarks>
/// All user-supplied values are validated and percent-encoded before being
/// concatenated. See <see cref="LaunchInputValidator"/> for CVE-2020-13699
/// hardening rules.
/// </remarks>
public static class UriSchemeBuilder
{
    public static string Build(ConnectionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        LaunchInputValidator.ValidateTeamViewerId(entry.TeamViewerId);

        var scheme = SchemeFor(entry.Mode);
        var uri = $"{scheme}?device={Uri.EscapeDataString(entry.TeamViewerId)}";

        if (!string.IsNullOrEmpty(entry.Password))
        {
            LaunchInputValidator.ValidatePassword(entry.Password);
            uri += $"&authorization={Uri.EscapeDataString(entry.Password)}";
        }

        return uri;
    }

    public static string SchemeFor(ConnectionMode mode) => mode switch
    {
        ConnectionMode.RemoteControl => "teamviewer10://control",
        ConnectionMode.FileTransfer => "tvfiletransfer1://",
        ConnectionMode.Vpn => "tvvpn1://",
        ConnectionMode.Chat => "tvchat1://",
        ConnectionMode.VideoCall => "tvvideocall1://",
        ConnectionMode.Presentation => "tvpresent1://",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown connection mode."),
    };

    /// <summary>True when the mode cannot be launched via CLI and must use a URI handler.</summary>
    public static bool IsUriOnly(ConnectionMode mode) =>
        mode is ConnectionMode.Chat or ConnectionMode.VideoCall or ConnectionMode.Presentation;
}
