using System.Windows;
using System.Windows.Controls;

namespace TeamStation.App.Mvvm;

public static class PasswordBoxBinding
{
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxBinding),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached("IsUpdating", typeof(bool), typeof(PasswordBoxBinding), new PropertyMetadata(false));

    private static readonly DependencyProperty IsHookedProperty =
        DependencyProperty.RegisterAttached("IsHooked", typeof(bool), typeof(PasswordBoxBinding), new PropertyMetadata(false));

    public static string GetBoundPassword(DependencyObject target) =>
        (string)(target.GetValue(BoundPasswordProperty) ?? string.Empty);

    public static void SetBoundPassword(DependencyObject target, string value) =>
        target.SetValue(BoundPasswordProperty, value);

    private static void OnBoundPasswordChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not PasswordBox box)
            return;

        if (!(bool)box.GetValue(IsHookedProperty))
        {
            box.PasswordChanged += HandlePasswordChanged;
            box.SetValue(IsHookedProperty, true);
        }

        if ((bool)box.GetValue(IsUpdatingProperty))
            return;

        var newPassword = e.NewValue as string ?? string.Empty;
        if (box.Password != newPassword)
            box.Password = newPassword;
    }

    private static void HandlePasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box)
            return;

        box.SetValue(IsUpdatingProperty, true);
        SetBoundPassword(box, box.Password);
        box.SetValue(IsUpdatingProperty, false);
    }
}
