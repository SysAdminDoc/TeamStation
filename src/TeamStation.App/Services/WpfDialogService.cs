using System.Windows;
using Microsoft.Win32;
using TeamStation.App.Views;
using TeamStation.Core.Models;

namespace TeamStation.App.Services;

public sealed class WpfDialogService : IDialogService
{
    public bool EditEntry(ConnectionEntry entry, Window? owner)
    {
        var dlg = new EntryEditorWindow(entry) { Owner = owner };
        return dlg.ShowDialog() == true;
    }

    public bool EditFolder(Folder folder, Window? owner)
    {
        var dlg = new FolderEditorWindow(folder) { Owner = owner };
        return dlg.ShowDialog() == true;
    }

    public string? ChooseExportPath(Window? owner)
    {
        var sfd = new SaveFileDialog
        {
            Title = "Export TeamStation backup",
            Filter = "TeamStation JSON (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
            FileName = $"teamstation-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            OverwritePrompt = true,
        };
        return sfd.ShowDialog(owner) == true ? sfd.FileName : null;
    }

    public string? ChooseActivityLogExportPath(Window? owner)
    {
        var sfd = new SaveFileDialog
        {
            Title = "Export activity log",
            Filter = "Newline-delimited JSON (*.ndjson)|*.ndjson|JSON Lines (*.jsonl)|*.jsonl|All files (*.*)|*.*",
            DefaultExt = ".ndjson",
            FileName = $"teamstation-activity-{DateTime.Now:yyyyMMdd-HHmmss}.ndjson",
            OverwritePrompt = true,
        };
        return sfd.ShowDialog(owner) == true ? sfd.FileName : null;
    }

    public string? ChooseImportPath(Window? owner)
    {
        var ofd = new OpenFileDialog
        {
            Title = "Import TeamStation backup",
            Filter = "TeamStation JSON (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
            CheckFileExists = true,
        };
        return ofd.ShowDialog(owner) == true ? ofd.FileName : null;
    }

    public string? ChooseImportCsvPath(Window? owner)
    {
        var ofd = new OpenFileDialog
        {
            Title = "Import CSV",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = ".csv",
            CheckFileExists = true,
        };
        return ofd.ShowDialog(owner) == true ? ofd.FileName : null;
    }

    public bool Confirm(
        Window? owner,
        string message,
        string title = "TeamStation",
        string confirmText = "OK",
        bool isDestructive = false) =>
        ThemedMessageDialog.Confirm(
            owner,
            title,
            message,
            isDestructive ? ThemedMessageKind.Danger : ThemedMessageKind.Warning,
            confirmText);

    public void ShowError(Window? owner, string title, string message) =>
        ThemedMessageDialog.Show(owner, title, message, ThemedMessageKind.Error);
}
