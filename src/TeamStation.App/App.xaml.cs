using System.IO;
using System.Windows;
using Microsoft.Win32;
using TeamStation.App.ViewModels;
using TeamStation.App.Views;
using TeamStation.Data.Security;
using TeamStation.Data.Storage;
using TeamStation.Launcher;

namespace TeamStation.App;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        try
        {
            var dbPath = StoragePaths.ResolveDatabasePath();
            var db = new Database(dbPath);
            var crypto = CryptoService.CreateOrLoad(db);
            var entries = new EntryRepository(db, crypto);
            var folders = new FolderRepository(db, crypto);
            var launcher = new TeamViewerLauncher();
            var tvExePath = TeamViewerPathResolver.Resolve();

            var vm = new MainViewModel(
                entries: entries,
                folders: folders,
                launcher: launcher,
                editEntryDialog: (entry, owner) =>
                {
                    var dlg = new EntryEditorWindow(entry) { Owner = owner };
                    return dlg.ShowDialog() == true;
                },
                editFolderDialog: (folder, owner) =>
                {
                    var dlg = new FolderEditorWindow(folder) { Owner = owner };
                    return dlg.ShowDialog() == true;
                },
                chooseExportPath: owner =>
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
                },
                chooseImportPath: owner =>
                {
                    var ofd = new OpenFileDialog
                    {
                        Title = "Import TeamStation backup",
                        Filter = "TeamStation JSON (*.json)|*.json|All files (*.*)|*.*",
                        DefaultExt = ".json",
                        CheckFileExists = true,
                    };
                    return ofd.ShowDialog(owner) == true ? ofd.FileName : null;
                },
                confirmDialog: (owner, message) =>
                    MessageBox.Show(owner!, message, "TeamStation",
                        MessageBoxButton.OKCancel, MessageBoxImage.Warning,
                        MessageBoxResult.Cancel) == MessageBoxResult.OK,
                tvExePath: tvExePath);

            var window = new MainWindow(vm);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "TeamStation failed to start.\n\n" + ex,
                "Startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
