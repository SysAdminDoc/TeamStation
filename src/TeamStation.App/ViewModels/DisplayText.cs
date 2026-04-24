using System.Collections.Generic;
using TeamStation.Core.Models;
using TeamStation.Launcher;

namespace TeamStation.App.ViewModels;

internal static class DisplayText
{
    public static string Count(int count, string singular, string? plural = null) =>
        $"{count} {(count == 1 ? singular : plural ?? singular + "s")}";

    public static string ModeLabel(ConnectionMode? value, string noneText = "Inherit from folder") => value switch
    {
        ConnectionMode.RemoteControl => "Remote control",
        ConnectionMode.FileTransfer => "File transfer",
        ConnectionMode.Vpn => "VPN",
        ConnectionMode.Chat => "Chat",
        ConnectionMode.VideoCall => "Video call",
        ConnectionMode.Presentation => "Presentation",
        null => noneText,
        _ => value.ToString() ?? noneText,
    };

    public static string QualityLabel(ConnectionQuality? value, string noneText = "Inherit from folder") => value switch
    {
        ConnectionQuality.AutoSelect => "Auto",
        ConnectionQuality.OptimizeQuality => "Optimize quality",
        ConnectionQuality.OptimizeSpeed => "Optimize speed",
        ConnectionQuality.CustomSettings => "Custom",
        ConnectionQuality.Undefined => "Undefined",
        null => noneText,
        _ => value.ToString() ?? noneText,
    };

    public static string AccessLabel(AccessControl? value, string noneText = "Inherit from folder") => value switch
    {
        AccessControl.FullAccess => "Full access",
        AccessControl.ConfirmAll => "Confirm all",
        AccessControl.ViewAndShow => "View and show",
        AccessControl.CustomSettings => "Custom settings",
        AccessControl.Undefined => "Undefined",
        null => noneText,
        _ => value.ToString() ?? noneText,
    };

    public static string RouteBadge(ConnectionMode? value) =>
        value is null ? "AUTO" : UriSchemeBuilder.IsUriOnly(value.Value) ? "URI" : "CLI";

    public static string RouteDescription(ConnectionMode? value)
    {
        if (value is null)
            return "Resolved automatically at launch";

        return UriSchemeBuilder.IsUriOnly(value.Value)
            ? "Uses the TeamViewer URI handler"
            : "Launches through TeamViewer.exe";
    }

    public static string FormatPath(FolderNode? folder)
    {
        if (folder is null)
            return "Top level";

        var segments = new Stack<string>();
        for (var cursor = folder; cursor is not null; cursor = cursor.Parent)
            segments.Push(cursor.Name);

        return string.Join(" / ", segments);
    }
}
