using System.Windows.Media;

namespace TeamStation.App.ViewModels;

public enum LogLevel { Info, Success, Warning, Error }

public sealed record LogEntry(DateTimeOffset At, LogLevel Level, string Message)
{
    public string TimeDisplay => At.LocalDateTime.ToString("HH:mm:ss");

    public string LevelTag => Level switch
    {
        LogLevel.Info => "info",
        LogLevel.Success => "ok",
        LogLevel.Warning => "warn",
        LogLevel.Error => "err",
        _ => "",
    };

    public Brush LevelBrush => Level switch
    {
        LogLevel.Success => TryBrush("GreenBrush", Brushes.LightGreen),
        LogLevel.Warning => TryBrush("YellowBrush", Brushes.Khaki),
        LogLevel.Error => TryBrush("RedBrush", Brushes.Salmon),
        _ => TryBrush("Subtext1Brush", Brushes.Silver),
    };

    private static Brush TryBrush(string key, Brush fallback)
        => System.Windows.Application.Current?.TryFindResource(key) as Brush ?? fallback;
}
