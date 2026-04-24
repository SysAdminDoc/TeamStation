using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using TeamStation.App.Services;
using TeamStation.App.ViewModels;
using TeamStation.App.Views;
using TeamStation.Data.Security;
using TeamStation.Data.Storage;
using TeamStation.Launcher;

namespace TeamStation.App;

public partial class App : Application
{
    // Per-user mutex; allows two different Windows accounts to run their own
    // instance on the same box but prevents the same user from stepping on
    // their own SQLite DB by accident. Local\ scope = per-session.
    private const string SingleInstanceMutexName = "Local\\TeamStation.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private TrayManager? _tray;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // Global unhandled-exception nets. We try to surface these as a
        // MessageBox instead of silently dropping into Windows Error Reporting,
        // which for a single-file self-contained WPF app looks like a vanish.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) ShowFatal(ex, "Unhandled error");
        };
        DispatcherUnhandledException += (_, args) =>
        {
            ShowFatal(args.Exception, "Unexpected error");
            args.Handled = true;
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ShowFatal(args.Exception, "Background task error");
            args.SetObserved();
        };

        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "TeamStation is already running. Look for the tray icon near the clock.",
                    "TeamStation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown(0);
                return;
            }

            var dbPath = StoragePaths.ResolveDatabasePath();
            var db = new Database(dbPath);
            var crypto = CryptoService.CreateOrLoad(db);
            var entries = new EntryRepository(db, crypto);
            var folders = new FolderRepository(db, crypto);
            var launcher = new TeamViewerLauncher();
            var tvExePath = TeamViewerPathResolver.Resolve();
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? "dev";

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
                chooseImportCsvPath: owner =>
                {
                    var ofd = new OpenFileDialog
                    {
                        Title = "Import CSV",
                        Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                        DefaultExt = ".csv",
                        CheckFileExists = true,
                    };
                    return ofd.ShowDialog(owner) == true ? ofd.FileName : null;
                },
                confirmDialog: (owner, message) =>
                    MessageBox.Show(owner!, message, "TeamStation",
                        MessageBoxButton.OKCancel, MessageBoxImage.Warning,
                        MessageBoxResult.Cancel) == MessageBoxResult.OK,
                tvExePath: tvExePath,
                startupVersion: version,
                startupDbPath: dbPath);

            var window = new MainWindow(vm);
            MainWindow = window;
            _tray = new TrayManager(window);
            window.Show();
        }
        catch (Exception ex)
        {
            ShowFatal(ex, "Startup error");
            Shutdown(1);
        }
    }

    private static void ShowFatal(Exception ex, string title)
    {
        var body = $"{ex.GetType().Name}: {ex.Message}\n\n{ex}";
        MessageBox.Show(body, $"TeamStation — {title}",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _tray?.Dispose(); } catch { /* swallow during exit */ }
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { /* not owned */ }
        try { _singleInstanceMutex?.Dispose(); } catch { /* swallow */ }
        base.OnExit(e);
    }
}
