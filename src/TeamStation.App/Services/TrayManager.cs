using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using TeamStation.App.ViewModels;
using TeamStation.Core.Models;
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
    private readonly MainViewModel _viewModel;
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ContextMenuStrip _menu;
    private readonly Icon _trayIcon;
    private readonly IntPtr _hIcon;
    private bool _disposed;

    public TrayManager(Window window, MainViewModel viewModel)
    {
        _window = window;
        _viewModel = viewModel;

        _trayIcon = CreateIcon(out _hIcon);
        _icon = new WinForms.NotifyIcon
        {
            Icon = _trayIcon,
            Text = "TeamStation",
            Visible = true,
        };

        _menu = new WinForms.ContextMenuStrip();
        _menu.Opening += (_, _) => RebuildMenu();
        _icon.ContextMenuStrip = _menu;

        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) ShowWindow();
        };

        _window.StateChanged += OnWindowStateChanged;
        _viewModel.TrayMenuInvalidated += OnTrayMenuInvalidated;
        RebuildMenu();
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

    private void OnTrayMenuInvalidated(object? sender, EventArgs e) => RebuildMenu();

    private void RebuildMenu()
    {
        if (_disposed)
            return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(RebuildMenu);
            return;
        }

        DisposeMenuItems();
        _menu.Items.Add("Show TeamStation", image: null, (_, _) => ShowWindow());
        _menu.Items.Add("Settings", image: null, (_, _) =>
        {
            ShowWindow();
            if (_viewModel.OpenSettingsCommand.CanExecute(null))
                _viewModel.OpenSettingsCommand.Execute(null);
        });

        AddEntrySection("Pinned", _viewModel.GetPinnedEntries());
        AddEntrySection("Recent", _viewModel.GetRecentEntries(limit: 8));

        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add("Exit", image: null, (_, _) =>
        {
            _window.StateChanged -= OnWindowStateChanged;
            Application.Current?.Shutdown();
        });
    }

    private void DisposeMenuItems()
    {
        // ContextMenuStrip.Items.Clear() drops references but leaves Component
        // lifetimes dangling; every rebuild would otherwise accumulate
        // undisposed ToolStripMenuItems for the life of the process.
        for (var i = _menu.Items.Count - 1; i >= 0; i--)
        {
            var item = _menu.Items[i];
            _menu.Items.RemoveAt(i);
            item.Dispose();
        }
    }

    private void AddEntrySection(string title, IReadOnlyList<ConnectionEntry> entries)
    {
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        var header = new WinForms.ToolStripMenuItem(title) { Enabled = false };
        _menu.Items.Add(header);

        if (entries.Count == 0)
        {
            _menu.Items.Add(new WinForms.ToolStripMenuItem("None") { Enabled = false });
            return;
        }

        foreach (var entry in entries.Take(10))
        {
            var item = new WinForms.ToolStripMenuItem(FormatEntry(entry)) { Enabled = _viewModel.IsTeamViewerReady };
            var entryId = entry.Id; // capture into closure, avoid holding entire ConnectionEntry
            item.Click += (_, _) =>
            {
                if (!_viewModel.IsTeamViewerReady)
                    return;

                _viewModel.LaunchEntryById(entryId);
            };
            _menu.Items.Add(item);
        }
    }

    private static string FormatEntry(ConnectionEntry entry)
    {
        var text = $"{entry.Name} ({entry.TeamViewerId})".Replace("&", "&&", StringComparison.Ordinal);
        return text.Length <= 64 ? text : string.Concat(text.AsSpan(0, 61), "...");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _window.StateChanged -= OnWindowStateChanged; } catch { /* window already disposed */ }
        try { _viewModel.TrayMenuInvalidated -= OnTrayMenuInvalidated; } catch { /* view model already disposed */ }
        try { _icon.Visible = false; } catch { /* best-effort */ }
        try { _icon.Dispose(); } catch { /* swallow */ }
        try { DisposeMenuItems(); } catch { /* swallow */ }
        try { _menu.Dispose(); } catch { /* swallow */ }
        try { _trayIcon.Dispose(); } catch { /* swallow */ }
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
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(0x1E, 0x1E, 0x2E));            // Catppuccin Base
            using var dot = new SolidBrush(Color.FromArgb(0x89, 0xB4, 0xFA)); // Blue
            g.FillEllipse(dot, 6, 6, size - 12, size - 12);
        }

        hIcon = bitmap.GetHicon();
        // Icon.FromHandle creates a managed Icon that shares the native handle;
        // we release the handle in Dispose via DestroyIcon. Clone so the tray
        // keeps a stable handle even if the source Icon is GC'd.
        using var shared = Icon.FromHandle(hIcon);
        return (Icon)shared.Clone();
    }
}
