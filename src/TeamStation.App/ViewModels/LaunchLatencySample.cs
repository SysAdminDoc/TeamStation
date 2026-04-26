namespace TeamStation.App.ViewModels;

public sealed record LaunchLatencySample(
    DateTimeOffset At,
    TimeSpan ToProcessStart,
    TimeSpan CredentialRead,
    TimeSpan HistoryWrite);
