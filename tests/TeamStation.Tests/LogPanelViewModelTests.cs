using TeamStation.App.ViewModels;

namespace TeamStation.Tests;

public class LogPanelViewModelTests
{
    [Fact]
    public void Launch_latency_summary_reports_last_percentiles_and_component_times()
    {
        var panel = new LogPanelViewModel();

        Assert.False(panel.HasLaunchLatency);

        panel.RecordLaunchLatency(new LaunchLatencySample(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(40),
            TimeSpan.FromMilliseconds(3),
            TimeSpan.FromMilliseconds(7)));
        panel.RecordLaunchLatency(new LaunchLatencySample(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(120),
            TimeSpan.FromMilliseconds(4),
            TimeSpan.FromMilliseconds(9)));
        panel.RecordLaunchLatency(new LaunchLatencySample(
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(18),
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(5)));

        Assert.True(panel.HasLaunchLatency);
        Assert.Contains("last 18 ms to start", panel.LaunchLatencySummary);
        Assert.Contains("p50 40 ms / p95 120 ms", panel.LaunchLatencySummary);
        Assert.Contains("Last credentials 1 ms, history DB 5 ms", panel.LaunchLatencySummary);
        Assert.Contains("0-50ms", panel.LaunchLatencyHistogram);
        Assert.Contains("100-250ms", panel.LaunchLatencyHistogram);
        Assert.Contains("##", panel.LaunchLatencyHistogram);
    }

    [Fact]
    public void Launch_latency_samples_keep_a_rolling_window()
    {
        var panel = new LogPanelViewModel();

        for (var i = 1; i <= 55; i++)
        {
            panel.RecordLaunchLatency(new LaunchLatencySample(
                DateTimeOffset.UtcNow,
                TimeSpan.FromMilliseconds(i),
                TimeSpan.Zero,
                TimeSpan.Zero));
        }

        Assert.Contains("across 50 launches", panel.LaunchLatencySummary);
        Assert.Contains("p50 30 ms / p95 53 ms", panel.LaunchLatencySummary);
    }
}
