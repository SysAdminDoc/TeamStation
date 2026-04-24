using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace TeamStation.App.Services;

/// <summary>
/// Owns the Windows system-tray icon. WPF has no native tray so we lean on
/// <see cref="WinForms.NotifyIcon"/> (included in the Windows Desktop SDK
/// once <c>UseWindowsForms=true</c> is set on the project).
/// </summary>
/// <remarks>
/// The icon is generated at runtime from a small <see cref="Bitmap"/> so the
/// repo doesn't need a committed <c>.ico</c> asset. A proper logo can drop
/// in later without touching this class.
/// </remarks>
public sealed class TrayManager : IDisposable
{
    private readonly Window _window;
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ContextMenuStrip _menu;
    private readonly IntPtr _hIcon;
    private bool _disposed;

    public TrayManager(Window window)
    {
        _window = window;

        var icon = CreateIcon(out _hIcon);
        _icon = new WinForms.NotifyIcon
        {
            Icon = icon,
            Text = "TeamStation",
            Visible = true,
        };

        _menu = new WinForms.ContextMenuStrip();
        _menu.Items.Add("Show TeamStation", image: null, (_, _) => ShowWindow());
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add("Exit", image: null, (_, _) =>
        {
            // Detach StateChanged before shutdown so we don't trip the
            // hide-to-tray logic during the normal exit path.
            _window.StateChanged -= OnWindowStateChanged;
            Application.Current.Shutdown();
        });
        _icon.ContextMenuStrip = _menu;

        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) ShowWindow();
        };

        _window.StateChanged += OnWindowStateChanged;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_window.WindowState == WindowState.Minimized)
            _window.Hide();
    }

    public void ShowWindow()
    {
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
        // The Topmost-toggle trick is a well-known WPF recipe to force the
        // window to the foreground without leaving it actually topmost.
        _window.Topmost = true;
        _window.Topmost = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _window.StateChanged -= OnWindowStateChanged; } catch { /* window already disposed */ }
        try { _icon.Visible = false; } catch { /* best-effort */ }
        try { _icon.Dispose(); } catch { /* swallow */ }
        try { _menu.Dispose(); } catch { /* swallow */ }
        if (_hIcon != IntPtr.Zero)
        {
            try { DestroyIcon(_hIcon); } catch { /* swallow */ }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private static Icon CreateIcon(out IntPtr hIcon)
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(0x1E, 0x1E, 0x2E));            // Catppuccin Base
        using var dot = new SolidBrush(Color.FromArgb(0x89, 0xB4, 0xFA)); // Blue
        g.FillEllipse(dot, 6, 6, size - 12, size - 12);

        hIcon = bitmap.GetHicon();
        // Icon.FromHandle creates a managed Icon that shares the native handle;
        // we release the handle in Dispose via DestroyIcon.
        return (Icon)Icon.FromHandle(hIcon).Clone();
    }
}
