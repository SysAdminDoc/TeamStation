using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace TeamStation.App.Mvvm;

/// <summary>
/// Attached property that keeps a <see cref="ListBox"/> scrolled to the
/// last item whenever its bound collection grows. The log panel uses this
/// so newly-emitted entries are always visible without the user scrolling.
/// </summary>
/// <remarks>
/// Binds to the ItemsSource's <see cref="INotifyCollectionChanged"/> events
/// via a one-shot wiring when the behavior is first enabled on a control.
/// Safe to set once; the subscription lives for the control's lifetime.
/// </remarks>
public static class AutoScrollBehavior
{
    public static readonly DependencyProperty AutoScrollToEndProperty =
        DependencyProperty.RegisterAttached(
            "AutoScrollToEnd",
            typeof(bool),
            typeof(AutoScrollBehavior),
            new PropertyMetadata(false, OnAutoScrollToEndChanged));

    public static void SetAutoScrollToEnd(DependencyObject element, bool value)
        => element.SetValue(AutoScrollToEndProperty, value);

    public static bool GetAutoScrollToEnd(DependencyObject element)
        => (bool)element.GetValue(AutoScrollToEndProperty);

    private static void OnAutoScrollToEndChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox) return;
        if (!(bool)e.NewValue) return;

        listBox.Loaded += (_, _) =>
        {
            if (listBox.ItemsSource is INotifyCollectionChanged notify)
                notify.CollectionChanged += (_, args) =>
                {
                    if (args.Action != NotifyCollectionChangedAction.Add) return;
                    var count = listBox.Items.Count;
                    if (count == 0) return;
                    var last = listBox.Items[count - 1];
                    listBox.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { listBox.ScrollIntoView(last); }
                        catch { /* swallow re-entrant scroll exceptions */ }
                    }));
                };
        };
    }
}
