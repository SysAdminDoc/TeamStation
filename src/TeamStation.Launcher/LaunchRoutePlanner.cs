using System.Text;
using TeamStation.Core.Models;

namespace TeamStation.Launcher;

public enum LaunchRoute
{
    Executable = 0,
    ProtocolHandler = 1,
}

public sealed record LaunchRoutePlan(
    LaunchRoute Route,
    string Badge,
    string Description,
    bool FellBackToExecutable);

/// <summary>
/// Chooses the least intrusive launch surface that still preserves the
/// connection's configured behavior.
/// </summary>
public static class LaunchRoutePlanner
{
    public static LaunchRoutePlan Plan(ConnectionEntry entry, LaunchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        options ??= LaunchOptions.Default;

        var mode = entry.Mode ?? ConnectionMode.RemoteControl;

        if (options.ForceUri)
        {
            return new LaunchRoutePlan(
                LaunchRoute.ProtocolHandler,
                "PROTOCOL",
                "Forced TeamViewer protocol-handler launch.",
                FellBackToExecutable: false);
        }

        if (UriSchemeBuilder.IsUriOnly(mode))
        {
            return new LaunchRoutePlan(
                LaunchRoute.ProtocolHandler,
                "PROTOCOL",
                "This mode is only available through the TeamViewer protocol handler.",
                FellBackToExecutable: false);
        }

        if (options.PreferProtocolHandler)
        {
            var executableOnlyReasons = ExecutableOnlyReasons(entry);
            if (executableOnlyReasons.Count == 0)
            {
                return new LaunchRoutePlan(
                    LaunchRoute.ProtocolHandler,
                    "PROTOCOL",
                    "Preferred TeamViewer protocol-handler launch.",
                    FellBackToExecutable: false);
            }

            return new LaunchRoutePlan(
                LaunchRoute.Executable,
                "EXE",
                $"Using TeamViewer.exe because {JoinReasons(executableOnlyReasons)}.",
                FellBackToExecutable: true);
        }

        return new LaunchRoutePlan(
            LaunchRoute.Executable,
            "EXE",
            "Using TeamViewer.exe launch.",
            FellBackToExecutable: false);
    }

    private static List<string> ExecutableOnlyReasons(ConnectionEntry entry)
    {
        var reasons = new List<string>(3);

        if (entry.Proxy is not null)
            reasons.Add("proxy settings require command-line flags");

        if (entry.Quality is { } quality &&
            quality != ConnectionQuality.AutoSelect &&
            Enum.IsDefined(quality))
        {
            reasons.Add("quality overrides require command-line flags");
        }

        if (entry.AccessControl is { } accessControl &&
            accessControl != AccessControl.Undefined &&
            Enum.IsDefined(accessControl))
        {
            reasons.Add("access-control overrides require command-line flags");
        }

        return reasons;
    }

    private static string JoinReasons(IReadOnlyList<string> reasons)
    {
        if (reasons.Count == 0)
            return string.Empty;
        if (reasons.Count == 1)
            return reasons[0];

        var builder = new StringBuilder();
        for (var i = 0; i < reasons.Count; i++)
        {
            if (i > 0)
                builder.Append(i == reasons.Count - 1 ? ", and " : ", ");
            builder.Append(reasons[i]);
        }

        return builder.ToString();
    }
}
