using System.Windows;
using TeamStation.Core.Models;

namespace TeamStation.App.Services;

/// <summary>
/// Owner-aware dialog factory used by the view-models. Replaces the tangle of
/// <c>Func&lt;Window?, ...&gt;</c> delegates that used to be plumbed through
/// <c>MainViewModel</c>'s constructor.
/// </summary>
public interface IDialogService
{
    bool EditEntry(ConnectionEntry entry, Window? owner);
    bool EditFolder(Folder folder, Window? owner);
    string? ChooseExportPath(Window? owner);
    string? ChooseImportPath(Window? owner);
    string? ChooseImportCsvPath(Window? owner);
    bool Confirm(Window? owner, string message);
    void ShowError(Window? owner, string title, string message);
}
