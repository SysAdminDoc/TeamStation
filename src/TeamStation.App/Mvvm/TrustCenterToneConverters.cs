using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using TeamStation.App.ViewModels;

namespace TeamStation.App.Mvvm;

/// <summary>
/// Maps a <see cref="TrustCenterTone"/> to the soft (background) brush keyed
/// in the active theme. Used by the Trust Center status pills.
/// </summary>
public sealed class TrustCenterToneToSoftBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is TrustCenterTone tone ? tone switch
        {
            TrustCenterTone.Healthy => "GreenSoftBrush",
            TrustCenterTone.Caution => "YellowSoftBrush",
            TrustCenterTone.Action => "RedSoftBrush",
            _ => "BlueSoftBrush",
        } : "BlueSoftBrush";

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maps a <see cref="TrustCenterTone"/> to the foreground / border brush.
/// </summary>
public sealed class TrustCenterToneToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is TrustCenterTone tone ? tone switch
        {
            TrustCenterTone.Healthy => "GreenBrush",
            TrustCenterTone.Caution => "YellowBrush",
            TrustCenterTone.Action => "RedBrush",
            _ => "Subtext1Brush",
        } : "Subtext1Brush";

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Short label rendered inside the status pill — single uppercase word so
/// the pill stays compact across all tones.
/// </summary>
public sealed class TrustCenterToneToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TrustCenterTone tone ? tone switch
        {
            TrustCenterTone.Healthy => "OK",
            TrustCenterTone.Caution => "CHECK",
            TrustCenterTone.Action => "ACTION",
            _ => "INFO",
        } : "INFO";

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
