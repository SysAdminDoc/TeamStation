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

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TeamStation.sln")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
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

    [Fact]
    public void Disabled_cloud_sync_button_still_explains_why_it_is_unavailable()
    {
        var doc = XDocument.Load(MainWindowXamlPath);
        var button = doc.Descendants(Wpf + "Button")
            .FirstOrDefault(b => ((string?)b.Attribute("Command")) == "{Binding SyncTeamViewerCloudCommand}");

        Assert.NotNull(button);
        Assert.Equal("{Binding CloudSyncStatusText}", (string?)button!.Attribute("ToolTip"));
        Assert.Equal("True", (string?)button.Attribute("ToolTipService.ShowOnDisabled"));
    }

    [Fact]
    public void Bulk_selection_surface_exposes_inline_actions()
    {
        var doc = XDocument.Load(MainWindowXamlPath);
        var summary = doc.Descendants(Wpf + "TextBlock")
            .FirstOrDefault(tb => ((string?)tb.Attribute("Text")) == "{Binding MultiSelectionSummary}");

        Assert.NotNull(summary);

        var commands = doc.Descendants(Wpf + "Button")
            .Select(b => (string?)b.Attribute("Command"))
            .Where(command => !string.IsNullOrWhiteSpace(command))
            .ToHashSet();

        Assert.Contains("{Binding BulkPinCommand}", commands);
        Assert.Contains("{Binding BulkUnpinCommand}", commands);
        Assert.Contains("{Binding ClearMultiSelectionCommand}", commands);
    }

    [Theory]
    [InlineData("InputDialog.xaml", "OkButton")]
    [InlineData("FolderPickerDialog.xaml", "OkButton")]
    [InlineData("FileSystemFolderDialog.xaml", "OkButton")]
    public void Workflow_dialog_disabled_primary_actions_explain_the_required_next_step(string xamlFile, string buttonName)
    {
        var path = Path.Combine(RepoRoot, "src", "TeamStation.App", "Views", xamlFile);
        var doc = XDocument.Load(path);
        var button = doc.Descendants(Wpf + "Button")
            .FirstOrDefault(b => ((string?)b.Attribute(Xaml + "Name")) == buttonName);

        Assert.NotNull(button);
        Assert.Equal("True", (string?)button!.Attribute("ToolTipService.ShowOnDisabled"));
        Assert.False(string.IsNullOrWhiteSpace((string?)button.Attribute("ToolTip")));
    }

    [Theory]
    [InlineData("FolderPickerDialog.xaml", "Tree", "Destination folders")]
    [InlineData("FileSystemFolderDialog.xaml", "FolderTree", "Local folders")]
    public void Picker_trees_are_single_tab_stops_with_screen_reader_names(string xamlFile, string treeName, string expectedName)
    {
        var path = Path.Combine(RepoRoot, "src", "TeamStation.App", "Views", xamlFile);
        var doc = XDocument.Load(path);
        var tree = doc.Descendants(Wpf + "TreeView")
            .FirstOrDefault(t => ((string?)t.Attribute(Xaml + "Name")) == treeName);

        Assert.NotNull(tree);
        Assert.Equal("Once", (string?)tree!.Attribute("KeyboardNavigation.TabNavigation"));
        Assert.Equal(expectedName, (string?)tree.Attribute("AutomationProperties.Name"));
    }

    [Theory]
    [InlineData("EntryEditorWindow.xaml", "NameBox", "Friendly name")]
    [InlineData("EntryEditorWindow.xaml", "IdBox", "TeamViewer ID")]
    [InlineData("EntryEditorWindow.xaml", "PasswordBox", "Connection password")]
    [InlineData("EntryEditorWindow.xaml", "ModeBox", "Connection mode")]
    [InlineData("EntryEditorWindow.xaml", "PathOverrideBox", "TeamViewer executable override")]
    [InlineData("FolderEditorWindow.xaml", "NameBox", "Folder name")]
    [InlineData("FolderEditorWindow.xaml", "DefaultPasswordBox", "Default password")]
    [InlineData("FolderEditorWindow.xaml", "DefaultPathBox", "Default TeamViewer executable path")]
    public void Editor_form_fields_expose_accessible_names(string xamlFile, string controlName, string expectedName)
    {
        var path = Path.Combine(RepoRoot, "src", "TeamStation.App", "Views", xamlFile);
        var doc = XDocument.Load(path);
        var control = doc.Descendants()
            .FirstOrDefault(e => ((string?)e.Attribute(Xaml + "Name")) == controlName);

        Assert.NotNull(control);
        Assert.Equal(expectedName, (string?)control!.Attribute("AutomationProperties.Name"));
    }

    [Fact]
    public void Folder_editor_validates_default_TeamViewer_path_before_saving()
    {
        var path = Path.Combine(RepoRoot, "src", "TeamStation.App", "Views", "FolderEditorWindow.xaml.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("File.Exists(defaultPath)", source);
        Assert.Contains("Default TeamViewer.exe path does not exist.", source);
    }

    [Theory]
    [InlineData("ThemeBox", "Theme")]
    [InlineData("TeamViewerPathBox", "TeamViewer executable")]
    [InlineData("ApiTokenBox", "TeamViewer Web API token")]
    [InlineData("CloudFolderBox", "Cloud sync folder")]
    [InlineData("SavedSearchesBox", "Saved searches")]
    [InlineData("ExternalToolsBox", "External tool definitions")]
    public void Settings_fields_expose_accessible_names(string controlName, string expectedName)
    {
        var path = Path.Combine(RepoRoot, "src", "TeamStation.App", "Views", "SettingsWindow.xaml");
        var doc = XDocument.Load(path);
        var control = doc.Descendants()
            .FirstOrDefault(e => ((string?)e.Attribute(Xaml + "Name")) == controlName);

        Assert.NotNull(control);
        Assert.Equal(expectedName, (string?)control!.Attribute("AutomationProperties.Name"));
    }

    [Fact]
    public void Settings_save_validates_paths_and_external_tool_lines()
    {
        var path = Path.Combine(RepoRoot, "src", "TeamStation.App", "Views", "SettingsWindow.xaml.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("File.Exists(teamViewerPath)", source);
        Assert.Contains("Directory.Exists(cloudFolder)", source);
        Assert.Contains("FindInvalidExternalToolLine", source);
        Assert.Contains("External tool line", source);
    }

    [Fact]
    public void Master_password_dialog_disables_continue_until_ready()
    {
        var path = Path.Combine(RepoRoot, "src", "TeamStation.App", "Views", "MasterPasswordWindow.xaml");
        var doc = XDocument.Load(path);
        var button = doc.Descendants(Wpf + "Button")
            .FirstOrDefault(b => ((string?)b.Attribute(Xaml + "Name")) == "ContinueButton");

        Assert.NotNull(button);
        Assert.Equal("False", (string?)button!.Attribute("IsEnabled"));
        Assert.Equal("True", (string?)button.Attribute("ToolTipService.ShowOnDisabled"));

        var password = doc.Descendants(Wpf + "PasswordBox")
            .FirstOrDefault(pb => ((string?)pb.Attribute(Xaml + "Name")) == "PasswordBox");
        Assert.NotNull(password);
        Assert.Equal("Master password", (string?)password!.Attribute("AutomationProperties.Name"));
    }

    [Fact]
    public void Themed_message_dialog_exposes_accessible_message_and_buttons()
    {
        var path = Path.Combine(RepoRoot, "src", "TeamStation.App", "Views", "ThemedMessageDialog.xaml");
        var doc = XDocument.Load(path);

        var window = doc.Root;
        Assert.NotNull(window);
        Assert.Equal("TeamStation dialog", (string?)window!.Attribute("AutomationProperties.Name"));

        var message = doc.Descendants(Wpf + "TextBlock")
            .FirstOrDefault(tb => ((string?)tb.Attribute(Xaml + "Name")) == "MessageText");
        Assert.NotNull(message);
        Assert.Equal("Dialog message", (string?)message!.Attribute("AutomationProperties.Name"));

        var cancel = doc.Descendants(Wpf + "Button")
            .FirstOrDefault(b => ((string?)b.Attribute(Xaml + "Name")) == "CancelButton");
        var confirm = doc.Descendants(Wpf + "Button")
            .FirstOrDefault(b => ((string?)b.Attribute(Xaml + "Name")) == "ConfirmButton");

        Assert.NotNull(cancel);
        Assert.NotNull(confirm);
        Assert.False(string.IsNullOrWhiteSpace((string?)cancel!.Attribute("AutomationProperties.Name")));
        Assert.False(string.IsNullOrWhiteSpace((string?)confirm!.Attribute("AutomationProperties.Name")));
    }

    [Fact]
    public void Destructive_message_dialog_defaults_focus_to_cancel()
    {
        var path = Path.Combine(RepoRoot, "src", "TeamStation.App", "Views", "ThemedMessageDialog.xaml.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("_kind == ThemedMessageKind.Danger", source);
        Assert.Contains("CancelButton.Focus()", source);
        Assert.Contains("ConfirmButton.IsDefault = false", source);
        Assert.Contains("CancelButton.IsDefault = true", source);
        Assert.Contains("This changes saved TeamStation data.", source);
    }

    [Fact]
    public void Activity_log_exposes_empty_state_and_disabled_clear_tooltip()
    {
        var doc = XDocument.Load(MainWindowXamlPath);

        var log = doc.Descendants(Wpf + "ListBox")
            .FirstOrDefault(lb => ((string?)lb.Attribute("AutomationProperties.Name")) == "Activity log");
        Assert.NotNull(log);
        Assert.Equal("{Binding LogHasEntries, Converter={StaticResource BoolToVis}}", (string?)log!.Attribute("Visibility"));

        var emptyState = doc.Descendants(Wpf + "StackPanel")
            .FirstOrDefault(sp => ((string?)sp.Attribute("AutomationProperties.Name")) == "Empty activity log");
        Assert.NotNull(emptyState);

        var clearButton = doc.Descendants(Wpf + "Button")
            .FirstOrDefault(b => ((string?)b.Attribute("Command")) == "{Binding ClearLogCommand}");
        Assert.NotNull(clearButton);
        Assert.Equal("{Binding LogClearTooltip}", (string?)clearButton!.Attribute("ToolTip"));
        Assert.Equal("True", (string?)clearButton.Attribute("ToolTipService.ShowOnDisabled"));
    }

    [Fact]
    public void Log_panel_view_model_rebroadcasts_empty_state_properties()
    {
        var logPanelPath = Path.Combine(RepoRoot, "src", "TeamStation.App", "ViewModels", "LogPanelViewModel.cs");
        var mainViewModelPath = Path.Combine(RepoRoot, "src", "TeamStation.App", "ViewModels", "MainViewModel.cs");
        var logPanelSource = File.ReadAllText(logPanelPath);
        var mainViewModelSource = File.ReadAllText(mainViewModelPath);

        Assert.Contains("public bool HasEntries => Entries.Count > 0;", logPanelSource);
        Assert.Contains("public string ClearTooltip", logPanelSource);
        Assert.Contains("OnPropertyChanged(nameof(HasEntries))", logPanelSource);
        Assert.Contains("public bool ShowLogEmptyState => !LogHasEntries;", mainViewModelSource);
        Assert.Contains("nameof(LogPanelViewModel.HasEntries)", mainViewModelSource);
    }
}
