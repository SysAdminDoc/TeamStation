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
    public void Activity_log_header_exposes_structured_export_action()
    {
        var doc = XDocument.Load(MainWindowXamlPath);
        var button = doc.Descendants(Wpf + "Button")
            .FirstOrDefault(b => ((string?)b.Attribute("Command")) == "{Binding ExportActivityLogCommand}");

        Assert.NotNull(button);
        Assert.Equal("Export", (string?)button!.Attribute("Content"));
        Assert.Equal("{Binding LogExportTooltip}", (string?)button.Attribute("ToolTip"));
        Assert.Equal("True", (string?)button.Attribute("ToolTipService.ShowOnDisabled"));
        Assert.Equal("Export activity log", (string?)button.Attribute("AutomationProperties.Name"));
    }

    [Fact]
    public void Database_folder_action_is_available_from_tools_menu_and_status_bar()
    {
        var doc = XDocument.Load(MainWindowXamlPath);
        var menuItem = doc.Descendants(Wpf + "MenuItem")
            .FirstOrDefault(mi => ((string?)mi.Attribute("Command")) == "{Binding OpenDatabaseFolderCommand}");
        var button = doc.Descendants(Wpf + "Button")
            .FirstOrDefault(b => ((string?)b.Attribute("Command")) == "{Binding OpenDatabaseFolderCommand}");

        Assert.NotNull(menuItem);
        Assert.Equal("Open database folder", (string?)menuItem!.Attribute("Header"));
        Assert.Equal("{Binding OpenDatabaseFolderTooltip}", (string?)menuItem.Attribute("ToolTip"));
        Assert.Equal("True", (string?)menuItem.Attribute("ToolTipService.ShowOnDisabled"));

        Assert.NotNull(button);
        Assert.Equal("{Binding OpenDatabaseFolderTooltip}", (string?)button!.Attribute("ToolTip"));
        Assert.Equal("True", (string?)button.Attribute("ToolTipService.ShowOnDisabled"));
        Assert.Equal("Open database folder", (string?)button.Attribute("AutomationProperties.Name"));
    }

    [Fact]
    public void Single_selection_surface_exposes_duplicate_action()
    {
        var doc = XDocument.Load(MainWindowXamlPath);
        var buttonCommands = doc.Descendants(Wpf + "Button")
            .Select(b => (string?)b.Attribute("Command"))
            .Where(command => !string.IsNullOrWhiteSpace(command))
            .ToList();

        Assert.Contains("{Binding CopySelectedIdCommand}", buttonCommands);
        Assert.Contains("{Binding DataContext.CopySelectedIdCommand, RelativeSource={RelativeSource AncestorType=Window}}", buttonCommands);
        Assert.Contains("{Binding DuplicateCommand}", buttonCommands);
        Assert.Contains("{Binding DataContext.DuplicateCommand, RelativeSource={RelativeSource AncestorType=Window}}", buttonCommands);

        var menuItems = doc.Descendants(Wpf + "MenuItem").ToList();
        Assert.Contains(menuItems, mi => (string?)mi.Attribute("Command") == "{Binding CopySelectedIdCommand}"
            && (string?)mi.Attribute("Header") == "Copy TeamViewer ID"
            && (string?)mi.Attribute("ToolTip") == "{Binding CopySelectedIdTooltip}"
            && (string?)mi.Attribute("ToolTipService.ShowOnDisabled") == "True");
        Assert.Contains(menuItems, mi => (string?)mi.Attribute("Command") == "{Binding CopySelectedIdCommand}"
            && (string?)mi.Attribute("Header") == "Copy TeamViewer ID");
        Assert.Contains(menuItems, mi => (string?)mi.Attribute("Command") == "{Binding DuplicateCommand}"
            && (string?)mi.Attribute("Header") == "Duplicate connection..."
            && (string?)mi.Attribute("ToolTip") == "{Binding DuplicateSelectionTooltip}"
            && (string?)mi.Attribute("ToolTipService.ShowOnDisabled") == "True");
        Assert.Contains(menuItems, mi => (string?)mi.Attribute("Command") == "{Binding DuplicateCommand}"
            && (string?)mi.Attribute("Header") == "Duplicate");
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
        Assert.Contains("{Binding BulkCopyIdsCommand}", commands);
        Assert.Contains("{Binding BulkMoveCommand}", commands);
        Assert.Contains("{Binding BulkDeleteCommand}", commands);
        Assert.Contains("{Binding BulkAddTagCommand}", commands);
        Assert.Contains("{Binding BulkRemoveTagCommand}", commands);
        Assert.Contains("{Binding BulkReplaceTagsCommand}", commands);
        Assert.Contains("{Binding BulkSetModeCommand}", commands);
        Assert.Contains("{Binding BulkSetQualityCommand}", commands);
        Assert.Contains("{Binding BulkSetAccessControlCommand}", commands);
        Assert.Contains("{Binding BulkSetProxyCommand}", commands);
        Assert.Contains("{Binding BulkClearProxyCommand}", commands);
        Assert.Contains("{Binding ClearMultiSelectionCommand}", commands);
    }

    [Fact]
    public void Bulk_actions_are_available_from_the_tree_context_menu()
    {
        var doc = XDocument.Load(MainWindowXamlPath);
        var items = doc.Descendants(Wpf + "MenuItem")
            .Where(mi => ((string?)mi.Attribute("Visibility")) == "{Binding IsBulkSelectionActive, Converter={StaticResource BoolToVis}}")
            .ToList();

        Assert.Contains(items, mi => (string?)mi.Attribute("Command") == "{Binding BulkMoveCommand}"
            && (string?)mi.Attribute("Header") == "{Binding BulkMoveSelectionLabel}");
        Assert.Contains(items, mi => (string?)mi.Attribute("Command") == "{Binding BulkCopyIdsCommand}"
            && (string?)mi.Attribute("Header") == "{Binding BulkCopyIdsSelectionLabel}");
        Assert.Contains(items, mi => (string?)mi.Attribute("Command") == "{Binding BulkDeleteCommand}"
            && (string?)mi.Attribute("Header") == "{Binding BulkDeleteSelectionLabel}");
        Assert.Contains(items, mi => (string?)mi.Attribute("Command") == "{Binding BulkAddTagCommand}"
            && (string?)mi.Attribute("Header") == "{Binding BulkAddTagSelectionLabel}");
        Assert.Contains(items, mi => (string?)mi.Attribute("Command") == "{Binding BulkRemoveTagCommand}"
            && (string?)mi.Attribute("Header") == "{Binding BulkRemoveTagSelectionLabel}");
        Assert.Contains(items, mi => (string?)mi.Attribute("Command") == "{Binding BulkReplaceTagsCommand}"
            && (string?)mi.Attribute("Header") == "{Binding BulkReplaceTagsSelectionLabel}");
        Assert.Contains(items, mi => (string?)mi.Attribute("Command") == "{Binding BulkSetModeCommand}"
            && (string?)mi.Attribute("Header") == "{Binding BulkSetModeSelectionLabel}");
        Assert.Contains(items, mi => (string?)mi.Attribute("Command") == "{Binding BulkSetQualityCommand}"
            && (string?)mi.Attribute("Header") == "{Binding BulkSetQualitySelectionLabel}");
        Assert.Contains(items, mi => (string?)mi.Attribute("Command") == "{Binding BulkSetAccessControlCommand}"
            && (string?)mi.Attribute("Header") == "{Binding BulkSetAccessControlSelectionLabel}");
        Assert.Contains(items, mi => (string?)mi.Attribute("Command") == "{Binding BulkSetProxyCommand}"
            && (string?)mi.Attribute("Header") == "{Binding BulkSetProxySelectionLabel}");
        Assert.Contains(items, mi => (string?)mi.Attribute("Command") == "{Binding BulkClearProxyCommand}"
            && (string?)mi.Attribute("Header") == "{Binding BulkClearProxySelectionLabel}");
    }

    [Fact]
    public void Main_view_model_bulk_commands_normalize_and_audit_updates()
    {
        var path = Path.Combine(RepoRoot, "src", "TeamStation.App", "ViewModels", "MainViewModel.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("BulkMoveCommand = new RelayCommand(BulkMove", source);
        Assert.Contains("BulkCopyIdsCommand = new RelayCommand(BulkCopyIds", source);
        Assert.Contains("BulkDeleteCommand = new RelayCommand(BulkDelete", source);
        Assert.Contains("BulkAddTagCommand = new RelayCommand(() => BulkEditTags(BulkTagOperation.Add)", source);
        Assert.Contains("BulkRemoveTagCommand = new RelayCommand(() => BulkEditTags(BulkTagOperation.Remove)", source);
        Assert.Contains("BulkReplaceTagsCommand = new RelayCommand(() => BulkEditTags(BulkTagOperation.Replace)", source);
        Assert.Contains("BulkSetModeCommand = new RelayCommand(BulkSetMode", source);
        Assert.Contains("BulkSetQualityCommand = new RelayCommand(BulkSetQuality", source);
        Assert.Contains("BulkSetAccessControlCommand = new RelayCommand(BulkSetAccessControl", source);
        Assert.Contains("BulkSetProxyCommand = new RelayCommand(BulkSetProxy", source);
        Assert.Contains("BulkClearProxyCommand = new RelayCommand(BulkClearProxy", source);
        Assert.Contains("ParseBulkTags", source);
        Assert.Contains("ChoiceDialog.Pick", source);
        Assert.Contains("FolderPickerDialog.Pick", source);
        Assert.Contains("BulkProxyDialog.Prompt", source);
        Assert.Contains("TryGetCommonLaunchValue", source);
        Assert.Contains("TryGetCommonProxy", source);
        Assert.Contains("System.Windows.Clipboard.SetDataObject", source);
        Assert.Contains("CreateModeOptions", source);
        Assert.Contains("CreateQualityOptions", source);
        Assert.Contains("CreateAccessControlOptions", source);
        Assert.Contains("Distinct(StringComparer.OrdinalIgnoreCase)", source);
        Assert.Contains("validationMessage: \"Enter at least one tag before applying.\"", source);
        Assert.Contains("bulk_move", source);
        Assert.Contains("bulk_copy_ids", source);
        Assert.Contains("bulk_delete", source);
        Assert.Contains("FormatBulkDeletePreview", source);
        Assert.Contains("Delete selected connections", source);
        Assert.Contains("bulk_add_tag", source);
        Assert.Contains("bulk_remove_tag", source);
        Assert.Contains("bulk_replace_tags", source);
        Assert.Contains("bulk_set_mode", source);
        Assert.Contains("bulk_set_quality", source);
        Assert.Contains("bulk_set_access_control", source);
        Assert.Contains("bulk_set_proxy", source);
        Assert.Contains("bulk_clear_proxy", source);
    }

    [Fact]
    public void Main_view_model_copy_id_commands_report_and_audit_clipboard_actions()
    {
        var path = Path.Combine(RepoRoot, "src", "TeamStation.App", "ViewModels", "MainViewModel.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("CopySelectedIdCommand = new RelayCommand(CopySelectedId", source);
        Assert.Contains("CopySelectedIdTooltip", source);
        Assert.Contains("private void CopySelectedId()", source);
        Assert.Contains("System.Windows.Clipboard.SetDataObject(id, copy: true)", source);
        Assert.Contains("\"copy_id\"", source);
        Assert.Contains("\"bulk_copy_ids\"", source);
        Assert.Contains("Copied TeamViewer ID to the clipboard.", source);
        Assert.Contains("Selected connection does not have a TeamViewer ID to copy.", source);
    }

    [Fact]
    public void Main_view_model_database_folder_command_opens_a_resolved_database_directory()
    {
        var path = Path.Combine(RepoRoot, "src", "TeamStation.App", "ViewModels", "MainViewModel.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("OpenDatabaseFolderCommand = new RelayCommand(OpenDatabaseFolder", source);
        Assert.Contains("CanOpenDatabaseFolder => TryGetOpenableDatabaseFolder", source);
        Assert.Contains("OpenDatabaseFolderTooltip", source);
        Assert.Contains("private void OpenDatabaseFolder()", source);
        Assert.Contains("Process.Start(new ProcessStartInfo", source);
        Assert.Contains("FileName = folder", source);
        Assert.Contains("UseShellExecute = true", source);
        Assert.Contains("\"database-folder\"", source);
        Assert.Contains("TryResolveDatabaseFolder", source);
        Assert.Contains("Path.GetFullPath(_startupDbPath)", source);
        Assert.Contains("Directory.Exists(folder)", source);
    }

    [Fact]
    public void Connection_control_surface_exposes_protocol_and_web_client_handoffs()
    {
        var doc = XDocument.Load(MainWindowXamlPath);
        var commands = doc.Descendants()
            .Select(e => (string?)e.Attribute("Command"))
            .Where(command => !string.IsNullOrWhiteSpace(command))
            .ToList();

        Assert.Contains("{Binding LaunchProtocolCommand}", commands);
        Assert.Contains("{Binding OpenTeamViewerWebClientCommand}", commands);
        Assert.Contains("{Binding DataContext.LaunchProtocolCommand, RelativeSource={RelativeSource AncestorType=Window}}", commands);
        Assert.Contains("{Binding DataContext.OpenTeamViewerWebClientCommand, RelativeSource={RelativeSource AncestorType=Window}}", commands);

        var mainPath = Path.Combine(RepoRoot, "src", "TeamStation.App", "ViewModels", "MainViewModel.cs");
        var mainSource = File.ReadAllText(mainPath);

        Assert.Contains("LaunchProtocolCommand = new RelayCommand(LaunchViaProtocol", mainSource);
        Assert.Contains("OpenTeamViewerWebClientCommand = new RelayCommand(OpenTeamViewerWebClient", mainSource);
        Assert.Contains("ForceUri: true", mainSource);
        Assert.Contains("TeamViewerWebClient.PortalUri.ToString()", mainSource);
        Assert.Contains("\"open_web_client\"", mainSource);
        Assert.Contains("OpenTeamViewerWebClientTooltip", mainSource);
        Assert.Contains("LaunchProtocolSelectedTooltip", mainSource);
    }

    [Fact]
    public void Main_view_model_duplicate_command_creates_clean_editable_copies()
    {
        var path = Path.Combine(RepoRoot, "src", "TeamStation.App", "ViewModels", "MainViewModel.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("DuplicateCommand = new RelayCommand(DuplicateSelectedEntry", source);
        Assert.Contains("CreateDuplicateEntry(entry.Model", source);
        Assert.Contains("BuildDuplicateName(source.Name", source);
        Assert.Contains("_dialogs.EditEntry(duplicate", source);
        Assert.Contains("IsPinned = false", source);
        Assert.Contains("Tags = source.Tags.ToList()", source);
        Assert.Contains("LastConnectedUtc = null", source);
        Assert.Contains("new ProxySettings(source.Proxy.Host, source.Proxy.Port, source.Proxy.Username, source.Proxy.Password)", source);
        Assert.Contains("\"duplicate\"", source);
    }

    [Fact]
    public void Input_dialog_supports_context_specific_validation_copy()
    {
        var path = Path.Combine(RepoRoot, "src", "TeamStation.App", "Views", "InputDialog.xaml.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("string validationMessage = \"Enter a name before saving.\"", source);
        Assert.Contains("_validationMessage = validationMessage;", source);
        Assert.Contains("ValidationText.Text = _validationMessage;", source);
    }

    [Fact]
    public void Choice_dialog_requires_an_explicit_value_when_selection_is_mixed()
    {
        var path = Path.Combine(RepoRoot, "src", "TeamStation.App", "Views", "ChoiceDialog.xaml.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("bool hasInitialValue", source);
        Assert.Contains("ChoiceBox.SelectedItem = options.FirstOrDefault", source);
        Assert.Contains("ApplyButton.IsEnabled = option is not null;", source);
        Assert.Contains("selectedValue = dlg.SelectedValue is T typedValue ? typedValue : null;", source);
    }

    [Fact]
    public void Bulk_proxy_dialog_reuses_the_hardened_launch_validators()
    {
        var path = Path.Combine(RepoRoot, "src", "TeamStation.App", "Views", "BulkProxyDialog.xaml.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("LaunchInputValidator.ValidateProxyEndpoint", source);
        Assert.Contains("LaunchInputValidator.ValidateProxyUsername", source);
        Assert.Contains("LaunchInputValidator.ValidatePassword", source);
        Assert.Contains("ProxyPasswordBox.Password", source);
        Assert.Contains("new ProxySettings", source);
    }

    [Theory]
    [InlineData("InputDialog.xaml", "OkButton")]
    [InlineData("ChoiceDialog.xaml", "ApplyButton")]
    [InlineData("BulkProxyDialog.xaml", "ApplyButton")]
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
    [InlineData("ProtocolLaunchBox", "Prefer TeamViewer protocol links")]
    [InlineData("CloudFolderBox", "Cloud sync folder")]
    [InlineData("RetentionDaysBox", "History retention days")]
    [InlineData("OptimizeDatabaseBox", "Optimize SQLite on connection close")]
    [InlineData("SlowQueryThresholdBox", "Slow SQLite query threshold")]
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
    public void Settings_surface_controls_history_retention()
    {
        var xamlPath = Path.Combine(RepoRoot, "src", "TeamStation.App", "Views", "SettingsWindow.xaml");
        var sourcePath = Path.Combine(RepoRoot, "src", "TeamStation.App", "Views", "SettingsWindow.xaml.cs");
        var xaml = File.ReadAllText(xamlPath);
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("History retention", xaml);
        Assert.Contains("Use 0 to keep history indefinitely.", xaml);
        Assert.Contains("RetentionDaysBox.Text = settings.HistoryRetentionDays.ToString()", source);
        Assert.Contains("OptimizeDatabaseBox.IsChecked = settings.OptimizeDatabaseOnClose", source);
        Assert.Contains("SlowQueryThresholdBox.Text = AppSettings.NormalizeSlowQueryThresholdMs(settings.SlowQueryThresholdMs).ToString()", source);
        Assert.Contains("_settings.HistoryRetentionDays = int.Parse(RetentionDaysBox.Text.Trim())", source);
        Assert.Contains("_settings.OptimizeDatabaseOnClose = OptimizeDatabaseBox.IsChecked == true", source);
        Assert.Contains("_settings.SlowQueryThresholdMs = AppSettings.NormalizeSlowQueryThresholdMs(int.Parse(SlowQueryThresholdBox.Text.Trim()))", source);
        Assert.Contains("retentionDays is < 0 or > 3650", source);
        Assert.Contains("History retention must be a whole number from 0 to 3650 days.", source);
        Assert.Contains("Slow query threshold", xaml);
        Assert.Contains("SQLite commands at or above this duration appear as activity warnings", xaml);
        Assert.Contains("slowQueryThresholdMs is < AppSettings.MinSlowQueryThresholdMs or > AppSettings.MaxSlowQueryThresholdMs", source);
        Assert.Contains("Slow query threshold must be a whole number", source);
    }

    [Fact]
    public void Startup_runs_database_integrity_check_before_main_window_is_shown()
    {
        var appPath = Path.Combine(RepoRoot, "src", "TeamStation.App", "App.xaml.cs");
        var vmPath = Path.Combine(RepoRoot, "src", "TeamStation.App", "ViewModels", "MainViewModel.cs");
        var appSource = File.ReadAllText(appPath);
        var vmSource = File.ReadAllText(vmPath);

        Assert.Contains("var startupIntegrity = db.CheckIntegrity()", appSource);
        Assert.Contains("SlowQueryThreshold = TimeSpan.FromMilliseconds(", appSource);
        Assert.Contains("startupIntegrityReport: startupIntegrity", appSource);
        Assert.Contains("AppendDatabaseIntegrityLog(startupIntegrityReport)", vmSource);
        Assert.Contains("report.IsOk ? LogLevel.Info : LogLevel.Warning", vmSource);
        Assert.Contains("_database.SlowQueryLogged += OnSlowQueryLogged", vmSource);
        Assert.Contains("Slow SQLite query (", vmSource);
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
    public void Activity_log_exposes_launch_latency_histogram()
    {
        var doc = XDocument.Load(MainWindowXamlPath);
        var histogram = doc.Descendants(Wpf + "StackPanel")
            .FirstOrDefault(sp => ((string?)sp.Attribute("AutomationProperties.Name")) == "Launch latency histogram");

        Assert.NotNull(histogram);
        Assert.Equal("{Binding HasLaunchLatency, Converter={StaticResource BoolToVis}}", (string?)histogram!.Attribute("Visibility"));

        var textBindings = histogram.Descendants(Wpf + "TextBlock")
            .Select(tb => (string?)tb.Attribute("Text"))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        Assert.Contains("{Binding LaunchLatencySummary}", textBindings);
        Assert.Contains("{Binding LaunchLatencyHistogram}", textBindings);
    }

    [Theory]
    [InlineData("{Binding ClearLogCommand}", "{Binding LogClearTooltip}")]
    [InlineData("{Binding SyncTeamViewerCloudCommand}", "{Binding CloudSyncStatusText}")]
    public void Secondary_menu_commands_explain_disabled_state(string command, string tooltip)
    {
        var doc = XDocument.Load(MainWindowXamlPath);
        var item = doc.Descendants(Wpf + "MenuItem")
            .FirstOrDefault(mi => ((string?)mi.Attribute("Command")) == command);

        Assert.NotNull(item);
        Assert.Equal(tooltip, (string?)item!.Attribute("ToolTip"));
        Assert.Equal("True", (string?)item.Attribute("ToolTipService.ShowOnDisabled"));
    }

    [Theory]
    [InlineData("{Binding ImportCsvCommand}", "Import a header-based CSV. Common aliases such as name, TeamViewer ID, folder, password, notes, and tags are auto-detected.")]
    [InlineData("{Binding ImportTeamViewerHistoryCommand}", "Scan local TeamViewer history files and import IDs that are not already saved.")]
    [InlineData("{Binding ImportCommand}", "Restore folders and connections from a TeamStation JSON backup. Matching IDs can be overwritten after confirmation.")]
    [InlineData("{Binding ExportCommand}", "Create a JSON backup of folders and connections. Plaintext password exports require confirmation.")]
    public void File_data_commands_explain_import_export_consequences(string command, string tooltip)
    {
        var doc = XDocument.Load(MainWindowXamlPath);
        var item = doc.Descendants(Wpf + "MenuItem")
            .FirstOrDefault(mi => ((string?)mi.Attribute("Command")) == command);

        Assert.NotNull(item);
        Assert.Equal(tooltip, (string?)item!.Attribute("ToolTip"));
    }

    [Fact]
    public void Session_export_menu_explains_disabled_state()
    {
        var doc = XDocument.Load(MainWindowXamlPath);
        var item = doc.Descendants(Wpf + "MenuItem")
            .FirstOrDefault(mi => ((string?)mi.Attribute("Command")) == "{Binding ExportSessionsCommand}");

        Assert.NotNull(item);
        Assert.Equal("{Binding SessionExportTooltip}", (string?)item!.Attribute("ToolTip"));
        Assert.Equal("True", (string?)item.Attribute("ToolTipService.ShowOnDisabled"));
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
        Assert.Contains("public bool HasLaunchLatency", logPanelSource);
        Assert.Contains("public string LaunchLatencyHistogram", logPanelSource);
        Assert.Contains("public bool ShowLogEmptyState => !LogHasEntries;", mainViewModelSource);
        Assert.Contains("public bool HasLaunchLatency => LogPanel.HasLaunchLatency;", mainViewModelSource);
        Assert.Contains("public string LaunchLatencySummary => LogPanel.LaunchLatencySummary;", mainViewModelSource);
        Assert.Contains("public string LaunchLatencyHistogram => LogPanel.LaunchLatencyHistogram;", mainViewModelSource);
        Assert.Contains("nameof(LogPanelViewModel.HasEntries)", mainViewModelSource);
        Assert.Contains("nameof(LogPanelViewModel.LaunchLatencyHistogram)", mainViewModelSource);
    }

    [Fact]
    public void Quick_connect_surface_explains_disabled_connect_state()
    {
        var doc = XDocument.Load(MainWindowXamlPath);

        var idBox = doc.Descendants(Wpf + "TextBox")
            .FirstOrDefault(tb => ((string?)tb.Attribute("Text"))?.Contains("QuickTeamViewerId") == true);
        Assert.NotNull(idBox);
        Assert.Equal("Quick connect TeamViewer ID", (string?)idBox!.Attribute("AutomationProperties.Name"));

        var connectButton = doc.Descendants(Wpf + "Button")
            .FirstOrDefault(b => ((string?)b.Attribute("Command")) == "{Binding QuickConnectCommand}");
        Assert.NotNull(connectButton);
        Assert.Equal("{Binding QuickConnectTooltip}", (string?)connectButton!.Attribute("ToolTip"));
        Assert.Equal("True", (string?)connectButton.Attribute("ToolTipService.ShowOnDisabled"));
        Assert.Equal("Connect quick TeamViewer session", (string?)connectButton.Attribute("AutomationProperties.Name"));
    }

    [Fact]
    public void Quick_connect_view_model_surfaces_readiness_copy()
    {
        var quickPath = Path.Combine(RepoRoot, "src", "TeamStation.App", "ViewModels", "QuickConnectViewModel.cs");
        var mainPath = Path.Combine(RepoRoot, "src", "TeamStation.App", "ViewModels", "MainViewModel.cs");
        var quickSource = File.ReadAllText(quickPath);
        var mainSource = File.ReadAllText(mainPath);

        Assert.Contains("public bool HasTeamViewerId", quickSource);
        Assert.Contains("public string ConnectTooltip", quickSource);
        Assert.Contains("Install or configure TeamViewer before launching.", quickSource);
        Assert.Contains("Enter a TeamViewer ID to connect.", quickSource);
        Assert.Contains("public string QuickConnectTooltip => QuickConnect.ConnectTooltip;", mainSource);
        Assert.Contains("nameof(QuickConnectViewModel.ConnectTooltip)", mainSource);
    }

    [Fact]
    public void Save_search_menu_explains_disabled_state()
    {
        var doc = XDocument.Load(MainWindowXamlPath);
        var item = doc.Descendants(Wpf + "MenuItem")
            .FirstOrDefault(mi => ((string?)mi.Attribute("Command")) == "{Binding SaveSearchCommand}");

        Assert.NotNull(item);
        Assert.Equal("{Binding SaveSearchTooltip}", (string?)item!.Attribute("ToolTip"));
        Assert.Equal("True", (string?)item.Attribute("ToolTipService.ShowOnDisabled"));
    }

    [Fact]
    public void Clear_search_menu_explains_disabled_state()
    {
        var doc = XDocument.Load(MainWindowXamlPath);
        var item = doc.Descendants(Wpf + "MenuItem")
            .FirstOrDefault(mi => ((string?)mi.Attribute("Command")) == "{Binding ClearSearchCommand}");

        Assert.NotNull(item);
        Assert.Equal("{Binding ClearSearchTooltip}", (string?)item!.Attribute("ToolTip"));
        Assert.Equal("True", (string?)item.Attribute("ToolTipService.ShowOnDisabled"));
    }

    [Fact]
    public void Search_view_model_prevents_duplicate_saved_search_feedback()
    {
        var searchPath = Path.Combine(RepoRoot, "src", "TeamStation.App", "ViewModels", "SearchViewModel.cs");
        var mainPath = Path.Combine(RepoRoot, "src", "TeamStation.App", "ViewModels", "MainViewModel.cs");
        var searchSource = File.ReadAllText(searchPath);
        var mainSource = File.ReadAllText(mainPath);

        Assert.Contains("public bool IsCurrentSearchSaved", searchSource);
        Assert.Contains("public bool CanSaveCurrent", searchSource);
        Assert.Contains("This search is already saved.", searchSource);
        Assert.Contains("if (_settings.SavedSearches.Contains(value, StringComparer.OrdinalIgnoreCase))", searchSource);
        Assert.Contains("return;", searchSource);
        Assert.Contains("public string SaveSearchTooltip => Search.SaveTooltip;", mainSource);
        Assert.Contains("nameof(SearchViewModel.SaveTooltip)", mainSource);
    }

    [Fact]
    public void Search_view_model_disables_clear_when_filter_is_empty()
    {
        var searchPath = Path.Combine(RepoRoot, "src", "TeamStation.App", "ViewModels", "SearchViewModel.cs");
        var mainPath = Path.Combine(RepoRoot, "src", "TeamStation.App", "ViewModels", "MainViewModel.cs");
        var searchSource = File.ReadAllText(searchPath);
        var mainSource = File.ReadAllText(mainPath);

        Assert.Contains("ClearCommand = new RelayCommand(() => SearchText = string.Empty, () => HasText)", searchSource);
        Assert.Contains("public string ClearTooltip", searchSource);
        Assert.Contains("No search filter to clear.", searchSource);
        Assert.Contains("ClearCommand.RaiseCanExecuteChanged()", searchSource);
        Assert.Contains("public string ClearSearchTooltip => Search.ClearTooltip;", mainSource);
        Assert.Contains("nameof(SearchViewModel.ClearTooltip)", mainSource);
    }

    [Fact]
    public void Backup_and_restore_confirmations_use_explicit_risk_copy()
    {
        var path = Path.Combine(RepoRoot, "src", "TeamStation.App", "ViewModels", "MainViewModel.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("Export plaintext backup", source);
        Assert.Contains("Export backup", source);
        Assert.Contains("Continue with the export?", source);
        Assert.Contains("Restore backup", source);
        Assert.Contains("Existing items with matching IDs will be overwritten. Review the file source before continuing.", source);
        Assert.Contains("isDestructive: true", source);
    }

    [Fact]
    public void History_import_and_session_export_surface_resilient_feedback()
    {
        var mainPath = Path.Combine(RepoRoot, "src", "TeamStation.App", "ViewModels", "MainViewModel.cs");
        var historyPath = Path.Combine(RepoRoot, "src", "TeamStation.Core", "Serialization", "TeamViewerHistoryImport.cs");
        var mainSource = File.ReadAllText(mainPath);
        var historySource = File.ReadAllText(historyPath);

        Assert.Contains("public static TeamViewerHistoryImportResult ScanFiles", historySource);
        Assert.Contains("ReadErrors", historySource);
        Assert.Contains("TeamViewerHistoryImport.ScanFiles", mainSource);
        Assert.Contains("TeamViewer history import failed. Review the activity panel for file access details.", mainSource);
        Assert.Contains("No readable TeamViewer history files were found.", mainSource);
        Assert.Contains("ExportSessionsCommand = new RelayCommand(ExportSessions, () => CanExportSessions)", mainSource);
        Assert.Contains("public bool CanExportSessions", mainSource);
        Assert.Contains("public string SessionExportTooltip", mainSource);
        Assert.Contains("Session export failed", mainSource);
    }

    [Theory]
    [InlineData("{Binding LaunchCommand}", "{Binding LaunchSelectedTooltip}")]
    [InlineData("{Binding EditCommand}", "{Binding EditSelectionTooltip}")]
    [InlineData("{Binding MoveCommand}", "{Binding MoveSelectionTooltip}")]
    [InlineData("{Binding DeleteCommand}", "{Binding DeleteSelectionTooltip}")]
    [InlineData("{Binding TogglePinCommand}", "{Binding PinSelectionTooltip}")]
    public void Selection_dependent_actions_explain_disabled_state(string command, string tooltip)
    {
        var doc = XDocument.Load(MainWindowXamlPath);
        var controls = doc.Descendants()
            .Where(e => ((string?)e.Attribute("Command")) == command)
            .ToList();

        Assert.NotEmpty(controls);
        Assert.Contains(controls, c => (string?)c.Attribute("ToolTip") == tooltip);
        Assert.Contains(controls, c => (string?)c.Attribute("ToolTipService.ShowOnDisabled") == "True");
    }

    [Fact]
    public void Main_view_model_surfaces_selection_action_tooltips()
    {
        var path = Path.Combine(RepoRoot, "src", "TeamStation.App", "ViewModels", "MainViewModel.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("public string LaunchSelectedTooltip", source);
        Assert.Contains("Select a connection to launch.", source);
        Assert.Contains("Install or configure TeamViewer before launching.", source);
        Assert.Contains("public string DeleteSelectionTooltip", source);
        Assert.Contains("nameof(LaunchSelectedTooltip)", source);
        Assert.Contains("nameof(DeleteSelectionTooltip)", source);
    }

    [Theory]
    [InlineData("EntryEditorWindow.xaml", "Enter a friendly name and TeamViewer ID.")]
    [InlineData("FolderEditorWindow.xaml", "Enter a folder name.")]
    public void Editor_primary_actions_explain_required_fields_when_disabled(string xamlFile, string expectedTooltip)
    {
        var path = Path.Combine(RepoRoot, "src", "TeamStation.App", "Views", xamlFile);
        var doc = XDocument.Load(path);
        var button = doc.Descendants(Wpf + "Button")
            .FirstOrDefault(b => ((string?)b.Attribute(Xaml + "Name")) == "SaveButton");

        Assert.NotNull(button);
        Assert.Equal("False", (string?)button!.Attribute("IsEnabled"));
        Assert.Equal(expectedTooltip, (string?)button.Attribute("ToolTip"));
        Assert.Equal("True", (string?)button.Attribute("ToolTipService.ShowOnDisabled"));
    }

    [Theory]
    [InlineData("EntryEditorWindow.xaml.cs", "UpdateSaveReadiness()", "NameBox.Text", "IdBox.Text")]
    [InlineData("FolderEditorWindow.xaml.cs", "UpdateSaveReadiness()", "NameBox.Text", "Enter a folder name.")]
    public void Editor_code_updates_primary_action_readiness(string sourceFile, string method, string requiredSnippet, string tooltipSnippet)
    {
        var path = Path.Combine(RepoRoot, "src", "TeamStation.App", "Views", sourceFile);
        var source = File.ReadAllText(path);

        Assert.Contains(method, source);
        Assert.Contains(requiredSnippet, source);
        Assert.Contains(tooltipSnippet, source);
        Assert.Contains("RequiredField_TextChanged", source);
    }
}
