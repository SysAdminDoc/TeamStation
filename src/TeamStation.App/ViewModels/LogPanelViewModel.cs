using System.Collections.ObjectModel;
using System.Windows.Data;
using TeamStation.App.Mvvm;

namespace TeamStation.App.ViewModels;

/// <summary>
/// The embedded 500-entry log panel: rolling append, severity-tagged, thread-safe.
/// Exposed on MainViewModel as <see cref="MainViewModel.LogPanel"/> and mirrored
/// through legacy top-level properties so XAML bindings keep working.
/// </summary>
public sealed class LogPanelViewModel : ViewModelBase
{
    private const int MaxLogEntries = 500;
    private const int MaxLaunchLatencySamples = 50;
    private readonly object _sync = new();
    private readonly Queue<LaunchLatencySample> _launchLatencySamples = new();
    private bool _isVisible;

    public LogPanelViewModel()
    {
        BindingOperations.EnableCollectionSynchronization(Entries, _sync);
        ClearCommand = new RelayCommand(Clear, () => Entries.Count > 0);
        ToggleCommand = new RelayCommand(() => IsVisible = !IsVisible);
    }

    public ObservableCollection<LogEntry> Entries { get; } = new();
    public bool HasEntries => Entries.Count > 0;

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetField(ref _isVisible, value))
                OnPropertyChanged(nameof(ButtonText));
        }
    }

    public string ButtonText => IsVisible ? "Hide activity" : "Show activity";

    public string Summary => Entries.Count == 0
        ? "No activity yet."
        : $"Showing the latest {Entries.Count} event{(Entries.Count == 1 ? string.Empty : "s")}.";

    public string ClearTooltip => HasEntries
        ? "Clear activity log"
        : "No activity to clear";

    public string ExportTooltip => HasEntries
        ? "Export activity log as newline-delimited JSON."
        : "No activity to export";

    public bool HasLaunchLatency => _launchLatencySamples.Count > 0;

    public string LaunchLatencySummary
    {
        get
        {
            if (_launchLatencySamples.Count == 0)
                return "No launch latency data yet.";

            var samples = _launchLatencySamples.ToArray();
            var startMs = samples
                .Select(sample => Milliseconds(sample.ToProcessStart))
                .Order()
                .ToArray();
            var last = samples[^1];
            return $"Launch latency: last {Milliseconds(last.ToProcessStart)} ms to start; " +
                   $"p50 {Percentile(startMs, 50)} ms / p95 {Percentile(startMs, 95)} ms " +
                   $"across {samples.Length} launch{(samples.Length == 1 ? string.Empty : "es")}. " +
                   $"Last credentials {Milliseconds(last.CredentialRead)} ms, history DB {Milliseconds(last.HistoryWrite)} ms.";
        }
    }

    public string LaunchLatencyHistogram
    {
        get
        {
            if (_launchLatencySamples.Count == 0)
                return string.Empty;

            var buckets = new[]
            {
                new LatencyBucket("0-50ms", 0),
                new LatencyBucket("50-100ms", 0),
                new LatencyBucket("100-250ms", 0),
                new LatencyBucket("250ms-1s", 0),
                new LatencyBucket("1s+", 0),
            };

            foreach (var sample in _launchLatencySamples)
            {
                var ms = Milliseconds(sample.ToProcessStart);
                var index = ms switch
                {
                    < 50 => 0,
                    < 100 => 1,
                    < 250 => 2,
                    < 1000 => 3,
                    _ => 4,
                };
                buckets[index] = buckets[index] with { Count = buckets[index].Count + 1 };
            }

            var max = Math.Max(1, buckets.Max(bucket => bucket.Count));
            return string.Join("  ", buckets.Select(bucket =>
            {
                var width = bucket.Count == 0 ? 0 : Math.Max(1, (int)Math.Round(bucket.Count / (double)max * 10));
                var bar = new string('#', width).PadRight(10);
                return $"{bucket.Label} {bar} {bucket.Count}";
            }));
        }
    }

    public RelayCommand ClearCommand { get; }
    public RelayCommand ToggleCommand { get; }

    public void Append(LogLevel level, string message)
    {
        Entries.Add(new LogEntry(DateTimeOffset.Now, level, message));
        while (Entries.Count > MaxLogEntries) Entries.RemoveAt(0);
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(ClearTooltip));
        OnPropertyChanged(nameof(ExportTooltip));
        ClearCommand.RaiseCanExecuteChanged();
    }

    public void RecordLaunchLatency(LaunchLatencySample sample)
    {
        _launchLatencySamples.Enqueue(sample);
        while (_launchLatencySamples.Count > MaxLaunchLatencySamples)
            _launchLatencySamples.Dequeue();

        OnPropertyChanged(nameof(HasLaunchLatency));
        OnPropertyChanged(nameof(LaunchLatencySummary));
        OnPropertyChanged(nameof(LaunchLatencyHistogram));
    }

    public void Clear()
    {
        Entries.Clear();
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(ClearTooltip));
        OnPropertyChanged(nameof(ExportTooltip));
        ClearCommand.RaiseCanExecuteChanged();
    }

    private static long Milliseconds(TimeSpan value) =>
        Math.Max(0, (long)Math.Round(value.TotalMilliseconds));

    private static long Percentile(IReadOnlyList<long> sortedValues, int percentile)
    {
        if (sortedValues.Count == 0)
            return 0;

        var rank = (int)Math.Ceiling(percentile / 100d * sortedValues.Count);
        var index = Math.Clamp(rank - 1, 0, sortedValues.Count - 1);
        return sortedValues[index];
    }

    private sealed record LatencyBucket(string Label, int Count);
}
