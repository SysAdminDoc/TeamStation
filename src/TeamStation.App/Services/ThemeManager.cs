using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.InteropServices;

namespace TeamStation.App.Services;

public sealed record AppTheme(string Id, string Name);

public static class ThemeManager
{
    private const int DwmAttributeUseImmersiveDarkMode = 20;
    private const int DwmAttributeUseImmersiveDarkModeBefore20H1 = 19;
    private static string _currentThemeId = "Dark";

    public static IReadOnlyList<AppTheme> Themes { get; } =
    [
        new("Dark", "Dark"),
        new("Graphite", "Graphite"),
        new("Light", "Light"),
        new("HighContrast", "High contrast"),
    ];

    private static readonly Dictionary<string, Palette> Palettes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dark"] = new(
            Base: Color.FromRgb(0x10, 0x10, 0x10),
            Mantle: Color.FromRgb(0x16, 0x16, 0x16),
            Crust: Color.FromRgb(0x0A, 0x0A, 0x0A),
            Surface0: Color.FromRgb(0x1E, 0x1E, 0x1E),
            Surface1: Color.FromRgb(0x28, 0x28, 0x28),
            Surface2: Color.FromRgb(0x36, 0x36, 0x36),
            Text: Color.FromRgb(0xF4, 0xF4, 0xF5),
            Subtext0: Color.FromRgb(0xA1, 0xA1, 0xAA),
            Subtext1: Color.FromRgb(0xD4, 0xD4, 0xD8),
            Overlay0: Color.FromRgb(0x71, 0x71, 0x7A),
            Blue: Color.FromRgb(0x58, 0xA6, 0xFF),
            Mauve: Color.FromRgb(0xC0, 0x84, 0xFC),
            Green: Color.FromRgb(0x7D, 0xD3, 0xA8),
            Red: Color.FromRgb(0xF8, 0x71, 0x71),
            Yellow: Color.FromRgb(0xF5, 0xC5, 0x42),
            BlueSoft: Color.FromArgb(0x24, 0x58, 0xA6, 0xFF),
            GreenSoft: Color.FromArgb(0x22, 0x7D, 0xD3, 0xA8),
            RedSoft: Color.FromArgb(0x24, 0xF8, 0x71, 0x71),
            YellowSoft: Color.FromArgb(0x24, 0xF5, 0xC5, 0x42),
            PanelBorder: Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF),
            InputBorder: Color.FromArgb(0x55, 0x8A, 0x8A, 0x8A)),
        ["Graphite"] = new(
            Base: Color.FromRgb(0x18, 0x18, 0x18),
            Mantle: Color.FromRgb(0x20, 0x20, 0x20),
            Crust: Color.FromRgb(0x0F, 0x0F, 0x0F),
            Surface0: Color.FromRgb(0x27, 0x27, 0x27),
            Surface1: Color.FromRgb(0x33, 0x33, 0x33),
            Surface2: Color.FromRgb(0x40, 0x40, 0x40),
            Text: Color.FromRgb(0xF5, 0xF5, 0xF5),
            Subtext0: Color.FromRgb(0xB0, 0xB0, 0xB0),
            Subtext1: Color.FromRgb(0xD6, 0xD6, 0xD6),
            Overlay0: Color.FromRgb(0x80, 0x80, 0x80),
            Blue: Color.FromRgb(0x4F, 0xC3, 0xF7),
            Mauve: Color.FromRgb(0xBA, 0xA7, 0xFF),
            Green: Color.FromRgb(0x8B, 0xD4, 0x8B),
            Red: Color.FromRgb(0xFF, 0x8A, 0x8A),
            Yellow: Color.FromRgb(0xE6, 0xC2, 0x66),
            BlueSoft: Color.FromArgb(0x25, 0x4F, 0xC3, 0xF7),
            GreenSoft: Color.FromArgb(0x22, 0x8B, 0xD4, 0x8B),
            RedSoft: Color.FromArgb(0x24, 0xFF, 0x8A, 0x8A),
            YellowSoft: Color.FromArgb(0x24, 0xE6, 0xC2, 0x66),
            PanelBorder: Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF),
            InputBorder: Color.FromArgb(0x55, 0x99, 0x99, 0x99)),
        ["Light"] = new(
            Base: Color.FromRgb(0xF4, 0xF5, 0xF7),
            Mantle: Color.FromRgb(0xFF, 0xFF, 0xFF),
            Crust: Color.FromRgb(0xE5, 0xE7, 0xEB),
            Surface0: Color.FromRgb(0xF9, 0xFA, 0xFB),
            Surface1: Color.FromRgb(0xEF, 0xF2, 0xF6),
            Surface2: Color.FromRgb(0xE4, 0xE8, 0xEF),
            Text: Color.FromRgb(0x1F, 0x29, 0x37),
            Subtext0: Color.FromRgb(0x6B, 0x72, 0x80),
            Subtext1: Color.FromRgb(0x37, 0x41, 0x51),
            Overlay0: Color.FromRgb(0x8B, 0x95, 0xA1),
            Blue: Color.FromRgb(0x1D, 0x64, 0xD8),
            Mauve: Color.FromRgb(0x7C, 0x3A, 0xB8),
            Green: Color.FromRgb(0x1C, 0x7C, 0x54),
            Red: Color.FromRgb(0xB9, 0x1C, 0x1C),
            Yellow: Color.FromRgb(0x9A, 0x67, 0x13),
            BlueSoft: Color.FromArgb(0x20, 0x1D, 0x64, 0xD8),
            GreenSoft: Color.FromArgb(0x18, 0x1C, 0x7C, 0x54),
            RedSoft: Color.FromArgb(0x18, 0xB9, 0x1C, 0x1C),
            YellowSoft: Color.FromArgb(0x18, 0x9A, 0x67, 0x13),
            PanelBorder: Color.FromArgb(0x30, 0x1F, 0x29, 0x37),
            InputBorder: Color.FromArgb(0x55, 0x6B, 0x72, 0x80)),
        ["HighContrast"] = new(
            Base: Colors.Black,
            Mantle: Color.FromRgb(0x08, 0x08, 0x08),
            Crust: Colors.Black,
            Surface0: Color.FromRgb(0x12, 0x12, 0x12),
            Surface1: Color.FromRgb(0x20, 0x20, 0x20),
            Surface2: Color.FromRgb(0x32, 0x32, 0x32),
            Text: Colors.White,
            Subtext0: Color.FromRgb(0xD0, 0xD0, 0xD0),
            Subtext1: Color.FromRgb(0xEA, 0xEA, 0xEA),
            Overlay0: Color.FromRgb(0xA0, 0xA0, 0xA0),
            Blue: Color.FromRgb(0x2F, 0xA8, 0xFF),
            Mauve: Color.FromRgb(0xD0, 0x9B, 0xFF),
            Green: Color.FromRgb(0x6B, 0xF2, 0x99),
            Red: Color.FromRgb(0xFF, 0x5C, 0x7A),
            Yellow: Color.FromRgb(0xFF, 0xD7, 0x52),
            BlueSoft: Color.FromArgb(0x38, 0x2F, 0xA8, 0xFF),
            GreenSoft: Color.FromArgb(0x2E, 0x6B, 0xF2, 0x99),
            RedSoft: Color.FromArgb(0x32, 0xFF, 0x5C, 0x7A),
            YellowSoft: Color.FromArgb(0x32, 0xFF, 0xD7, 0x52),
            PanelBorder: Color.FromArgb(0x75, 0xFF, 0xFF, 0xFF),
            InputBorder: Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
    };

    public static string Normalize(string? themeId) =>
        themeId is not null && Palettes.ContainsKey(themeId) ? themeId : "Dark";

    public static void Apply(string? themeId)
    {
        if (Application.Current is not { } app)
            return;

        _currentThemeId = Normalize(themeId);
        var palette = Palettes[_currentThemeId];
        Set(app, nameof(Palette.Base), palette.Base);
        Set(app, nameof(Palette.Mantle), palette.Mantle);
        Set(app, nameof(Palette.Crust), palette.Crust);
        Set(app, nameof(Palette.Surface0), palette.Surface0);
        Set(app, nameof(Palette.Surface1), palette.Surface1);
        Set(app, nameof(Palette.Surface2), palette.Surface2);
        Set(app, nameof(Palette.Text), palette.Text);
        Set(app, nameof(Palette.Subtext0), palette.Subtext0);
        Set(app, nameof(Palette.Subtext1), palette.Subtext1);
        Set(app, nameof(Palette.Overlay0), palette.Overlay0);
        Set(app, nameof(Palette.Blue), palette.Blue);
        Set(app, nameof(Palette.Mauve), palette.Mauve);
        Set(app, nameof(Palette.Green), palette.Green);
        Set(app, nameof(Palette.Red), palette.Red);
        Set(app, nameof(Palette.Yellow), palette.Yellow);
        Set(app, nameof(Palette.BlueSoft), palette.BlueSoft);
        Set(app, nameof(Palette.GreenSoft), palette.GreenSoft);
        Set(app, nameof(Palette.RedSoft), palette.RedSoft);
        Set(app, nameof(Palette.YellowSoft), palette.YellowSoft);
        Set(app, nameof(Palette.PanelBorder), palette.PanelBorder);
        Set(app, nameof(Palette.InputBorder), palette.InputBorder);

        foreach (Window window in app.Windows)
            ApplyWindowChrome(window);
    }

    public static void ConfigureWindow(Window window)
    {
        window.SetResourceReference(Window.BackgroundProperty, "BaseBrush");
        window.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        window.SourceInitialized += (_, _) => ApplyWindowChrome(window);
        ApplyWindowChrome(window);
    }

    private static void Set(Application app, string key, Color color)
    {
        app.Resources[key] = color;
        var brushKey = key + "Brush";
        if (app.Resources.Contains(brushKey) && app.Resources[brushKey] is SolidColorBrush brush && !brush.IsFrozen)
        {
            // Fast path: the brush was declared mutable and is not yet sealed
            // by a parent Freezable (e.g. a ControlTemplate). Update in-place
            // so every StaticResource consumer picks the change up automatically.
            brush.Color = color;
            return;
        }

        // Fallback path: brush either does not exist or was frozen when a
        // ControlTemplate that referenced it at parse time was sealed. Swap in
        // a fresh mutable brush — any DynamicResource consumer picks the new
        // reference up automatically; StaticResource consumers that hold the
        // old frozen brush will not re-theme until the view is re-created,
        // which is acceptable for the rare freeze path.
        app.Resources[brushKey] = new SolidColorBrush(color);
    }

    private static void ApplyWindowChrome(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            var useDarkTitleBar = string.Equals(_currentThemeId, "Light", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            if (DwmSetWindowAttribute(hwnd, DwmAttributeUseImmersiveDarkMode, ref useDarkTitleBar, sizeof(int)) != 0)
                _ = DwmSetWindowAttribute(hwnd, DwmAttributeUseImmersiveDarkModeBefore20H1, ref useDarkTitleBar, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    private sealed record Palette(
        Color Base,
        Color Mantle,
        Color Crust,
        Color Surface0,
        Color Surface1,
        Color Surface2,
        Color Text,
        Color Subtext0,
        Color Subtext1,
        Color Overlay0,
        Color Blue,
        Color Mauve,
        Color Green,
        Color Red,
        Color Yellow,
        Color BlueSoft,
        Color GreenSoft,
        Color RedSoft,
        Color YellowSoft,
        Color PanelBorder,
        Color InputBorder);
}
