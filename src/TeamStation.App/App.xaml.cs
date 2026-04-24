using System.Windows;
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
            var launcher = new TeamViewerLauncher();
            var tvExePath = TeamViewerPathResolver.Resolve();

            var vm = new MainViewModel(
                entries: entries,
                launcher: launcher,
                editDialog: (entry, owner) =>
                {
                    var dlg = new EntryEditorWindow(entry) { Owner = owner };
                    return dlg.ShowDialog() == true;
                },
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
