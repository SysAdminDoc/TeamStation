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
/// Wires a single <see cref="INotifyCollectionChanged.CollectionChanged"/>
/// handler per <see cref="ListBox"/>. Re-enabling the behavior no-ops; a
/// subsequent <c>ItemsSource</c> swap reattaches to the new collection. The
/// <c>Unloaded</c> lifecycle detaches the handler so the ListBox can be GC'd.
/// </remarks>
public static class AutoScrollBehavior
{
    public static readonly DependencyProperty AutoScrollToEndProperty =
        DependencyProperty.RegisterAttached(
            "AutoScrollToEnd",
            typeof(bool),
            typeof(AutoScrollBehavior),
            new PropertyMetadata(false, OnAutoScrollToEndChanged));

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(AutoScrollState),
            typeof(AutoScrollBehavior),
            new PropertyMetadata(null));

    public static void SetAutoScrollToEnd(DependencyObject element, bool value)
        => element.SetValue(AutoScrollToEndProperty, value);

    public static bool GetAutoScrollToEnd(DependencyObject element)
        => (bool)element.GetValue(AutoScrollToEndProperty);

    private static void OnAutoScrollToEndChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox) return;
        var enabled = (bool)e.NewValue;
        var state = listBox.GetValue(StateProperty) as AutoScrollState;

        if (enabled)
        {
            if (state is not null) return; // already wired
            state = new AutoScrollState(listBox);
            listBox.SetValue(StateProperty, state);
            state.Attach();
        }
        else if (state is not null)
        {
            state.Detach();
            listBox.ClearValue(StateProperty);
        }
    }

    private sealed class AutoScrollState
    {
        private readonly ListBox _listBox;
        private INotifyCollectionChanged? _observed;

        public AutoScrollState(ListBox listBox)
        {
            _listBox = listBox;
        }

        public void Attach()
        {
            _listBox.Loaded += OnLoaded;
            _listBox.Unloaded += OnUnloaded;
            // ItemsSource may already be set (common for bound log panels that
            // construct their data context before the control loads).
            SubscribeToItemsSource();
            _listBox.DataContextChanged += OnDataContextChanged;
        }

        public void Detach()
        {
            _listBox.Loaded -= OnLoaded;
            _listBox.Unloaded -= OnUnloaded;
            _listBox.DataContextChanged -= OnDataContextChanged;
            UnsubscribeFromItemsSource();
        }

        private void OnLoaded(object? sender, RoutedEventArgs e) => SubscribeToItemsSource();
        private void OnUnloaded(object? sender, RoutedEventArgs e) => UnsubscribeFromItemsSource();
        private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e) => SubscribeToItemsSource();

        private void SubscribeToItemsSource()
        {
            var incoming = _listBox.ItemsSource as INotifyCollectionChanged;
            if (ReferenceEquals(incoming, _observed)) return;

            UnsubscribeFromItemsSource();

            if (incoming is null) return;
            _observed = incoming;
            _observed.CollectionChanged += OnCollectionChanged;
        }

        private void UnsubscribeFromItemsSource()
        {
            if (_observed is null) return;
            _observed.CollectionChanged -= OnCollectionChanged;
            _observed = null;
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add) return;
            var count = _listBox.Items.Count;
            if (count == 0) return;
            var last = _listBox.Items[count - 1];
            _listBox.Dispatcher.BeginInvoke(new Action(() =>
            {
                try { _listBox.ScrollIntoView(last); }
                catch { /* re-entrant scroll during virtualization; ignore */ }
            }));
        }
    }
}
