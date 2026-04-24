using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace TeamStation.Tests;

/// <summary>
/// Pins the v0.3.3 A11y baseline: arrow-key tree nav (built into WPF), plus
/// single-key Enter / F2 / Delete on the focused TreeViewItem. Matches
/// Explorer + VS Code conventions; the project's "no keyboard shortcuts"
/// rule explicitly disallows chord bindings (Ctrl/Alt/Shift modifiers), so
/// we also assert nothing modifier-bound sneaks in.
///
/// The tests parse <c>MainWindow.xaml</c> as XML so we don't need a WPF
/// runtime / STA thread to run them. Structural — robust against whitespace
/// changes; brittle only if someone restructures the TreeView definition,
/// which is exactly when we want a heads-up.
/// </summary>
public class MainWindowKeyboardNavTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string MainWindowXamlPath
    {
        get
        {
            // Walk up from the test assembly until we find the repo root.
            var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TeamStation.sln")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            var path = Path.Combine(dir!.FullName, "src", "TeamStation.App", "MainWindow.xaml");
            Assert.True(File.Exists(path), $"MainWindow.xaml not found at {path}");
            return path;
        }
    }

    private static XElement TreeView()
    {
        var doc = XDocument.Load(MainWindowXamlPath);
        var tree = doc.Descendants(Wpf + "TreeView")
            .FirstOrDefault(t => (string?)t.Attribute(Xaml + "Name") == "Tree");
        Assert.NotNull(tree);
        return tree!;
    }

    [Fact]
    public void Tree_declares_three_single_key_bindings_for_a11y()
    {
        var tree = TreeView();
        var inputBindings = tree.Element(Wpf + "TreeView.InputBindings");
        Assert.NotNull(inputBindings);

        var keyBindings = inputBindings!.Elements(Wpf + "KeyBinding").ToList();
        Assert.Equal(3, keyBindings.Count);

        var keys = keyBindings.Select(kb => (string?)kb.Attribute("Key")).OrderBy(k => k).ToArray();
        Assert.Equal(new[] { "Delete", "Enter", "F2" }, keys);

        // No chord modifiers — project rule: "no keyboard shortcuts".
        Assert.All(keyBindings, kb =>
        {
            var mod = (string?)kb.Attribute("Modifiers");
            Assert.True(string.IsNullOrEmpty(mod), $"KeyBinding for {(string?)kb.Attribute("Key")} has modifiers '{mod}' — single-key only");
        });
    }

    [Fact]
    public void Enter_is_bound_to_LaunchCommand_F2_to_RenameCommand_Delete_to_DeleteCommand()
    {
        var tree = TreeView();
        var keyBindings = tree.Element(Wpf + "TreeView.InputBindings")!
            .Elements(Wpf + "KeyBinding")
            .ToDictionary(kb => (string)kb.Attribute("Key")!, kb => (string?)kb.Attribute("Command"));

        Assert.Equal("{Binding LaunchCommand}", keyBindings["Enter"]);
        Assert.Equal("{Binding RenameCommand}", keyBindings["F2"]);
        Assert.Equal("{Binding DeleteCommand}", keyBindings["Delete"]);
    }

    [Fact]
    public void Tree_exposes_an_AutomationProperties_Name_for_screen_readers()
    {
        var tree = TreeView();
        var auto = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        // AutomationProperties.Name is namespace-qualified attribute; XLinq surfaces it without ns
        var name = (string?)tree.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml/presentation"))
                ?? (string?)tree.Attribute("AutomationProperties.Name");
        Assert.False(string.IsNullOrWhiteSpace(name), "Tree must expose an AutomationProperties.Name for screen readers");
        Assert.Contains("Connections", name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Search_textbox_exposes_an_AutomationProperties_Name_for_screen_readers()
    {
        var doc = XDocument.Load(MainWindowXamlPath);
        // Find the search TextBox by its SearchText binding — that's the
        // canonical signature of the search input even if its position in the
        // grid changes. Then check it carries AutomationProperties.Name.
        var searchBox = doc.Descendants(Wpf + "TextBox")
            .FirstOrDefault(tb => ((string?)tb.Attribute("Text"))?.Contains("SearchText") == true);
        Assert.NotNull(searchBox);

        var name = (string?)searchBox!.Attribute("AutomationProperties.Name");
        Assert.False(string.IsNullOrWhiteSpace(name), "Search TextBox must expose AutomationProperties.Name");
        Assert.Contains("Search", name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tree_uses_KeyboardNavigation_TabNavigation_Once_to_avoid_tab_trap()
    {
        // "Once" makes the entire tree a single tab stop; arrow keys navigate
        // within. Without this, Tab inside the tree would step through every
        // visible TreeViewItem — keyboard users would have to Tab past
        // hundreds of entries to reach the detail pane.
        var tree = TreeView();
        var tab = (string?)tree.Attribute("KeyboardNavigation.TabNavigation");
        Assert.Equal("Once", tab);
    }
}
