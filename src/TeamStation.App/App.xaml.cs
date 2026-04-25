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
    private bool _ownsSingleInstanceMutex;
    private TrayManager? _tray;
    private CryptoService? _crypto;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        // Global unhandled-exception nets. We try to surface these in-app
        // instead of silently dropping into Windows Error Reporting,
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
            _ownsSingleInstanceMutex = createdNew;
            if (!createdNew)
            {
                ThemeManager.Apply("System");
                ThemedMessageDialog.Show(
                    null,
                    "TeamStation",
                    "TeamStation is already running. Look for the tray icon near the clock.",
                    ThemedMessageKind.Info);
                Shutdown(0);
                return;
            }

            var dbPath = StoragePaths.ResolveDatabasePath();
            var settingsService = new SettingsService(StoragePaths.ResolveSettingsPath());
            var settings = settingsService.Load();
            settings.Theme = ThemeManager.Normalize(settings.Theme);
            ThemeManager.Apply(settings.Theme);
            if (!settings.HasAcceptedLaunchNotice)
            {
                var accepted = ThemedMessageDialog.Confirm(
                    null,
                    "TeamStation first run",
                    "TeamStation is a shortcut manager for the official TeamViewer client. It stores credentials locally, launches the unmodified TeamViewer app, and does not inspect or relay sessions.\n\nSaved passwords are encrypted at rest, but TeamViewer still receives credentials during launch. Continue?",
                    ThemedMessageKind.Info,
                    "Continue");
                if (!accepted)
                {
                    Shutdown(0);
                    return;
                }

                settings.HasAcceptedLaunchNotice = true;
                settingsService.Save(settings);
            }

            var db = new Database(dbPath)
            {
                OptimizeOnConnectionClose = settings.OptimizeDatabaseOnClose,
            };
            _crypto = CreateCrypto(db);
            var crypto = _crypto;

            // Lazy Unprotect of the TeamViewer API token. The entropy salt
            // for the AppSettings DPAPI wrap lives in the SQLite _meta table
            // (same row CryptoService uses for the DEK wrap), which has just
            // come into scope. SettingsService.Load left
            // settings.TeamViewerApiToken null — fill it in now so any code
            // path further down (cloud sync, online-state polling) sees the
            // unwrapped token.
            settingsService.Entropy = db.LoadValue("dpapi_entropy_v1");
            settingsService.UnprotectApiToken(settings);
            var entries = new EntryRepository(db, crypto);
            var folders = new FolderRepository(db, crypto);
            var sessions = new SessionRepository(db);
            var audit = new AuditLogRepository(db);
            var launcher = new TeamViewerLauncher(() => ResolveTeamViewerPath(settings));
            var tvExePath = ResolveTeamViewerPath(settings);
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? "dev";

            var vm = new MainViewModel(
                entries: entries,
                folders: folders,
                launcher: launcher,
                dialogs: new WpfDialogService(),
                settings: settings,
                settingsService: settingsService,
                sessions: sessions,
                auditLog: audit,
                database: db,
                tvExePath: tvExePath,
                startupVersion: version,
                startupDbPath: dbPath);

            var window = new MainWindow(vm);
            MainWindow = window;
            _tray = new TrayManager(window, vm);
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
        try
        {
            var dumpPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "teamstation-fatal.log");
            System.IO.File.AppendAllText(dumpPath, $"[{DateTime.Now:O}] {title}\n{body}\n\n");
        }
        catch { /* best-effort - do not block the user-facing error */ }

        try
        {
            ThemedMessageDialog.Show(Application.Current?.MainWindow, $"TeamStation - {title}", body, ThemedMessageKind.Error);
        }
        catch
        {
            MessageBox.Show(body, $"TeamStation - {title}",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string? ResolveTeamViewerPath(AppSettings settings)
    {
        return !string.IsNullOrWhiteSpace(settings.TeamViewerPathOverride) && File.Exists(settings.TeamViewerPathOverride)
            ? settings.TeamViewerPathOverride
            : TeamViewerPathResolver.Resolve();
    }

    private static CryptoService CreateCrypto(Database db)
    {
        if (!StoragePaths.IsPortable(out _))
            return CryptoService.CreateOrLoad(db);

        var hasMasterPassword = CryptoService.HasMasterPassword(db);
        var dialog = new MasterPasswordWindow(createNew: !hasMasterPassword);
        if (dialog.ShowDialog() != true)
            throw new InvalidOperationException("A master password is required in portable mode.");

        return CryptoService.CreateOrLoad(db, CryptoUnlockOptions.WithMasterPassword(dialog.Password));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _tray?.Dispose(); } catch { /* swallow during exit */ }
        if (_ownsSingleInstanceMutex)
        {
            try { _singleInstanceMutex?.ReleaseMutex(); } catch { /* swallow during exit */ }
        }
        try { _singleInstanceMutex?.Dispose(); } catch { /* swallow during exit */ }
        // Zero the DEK last so any swap file snapshot captured during a
        // normal exit cannot recover the unwrapped key. Repositories dropped
        // their reference when the view model went out of scope; the DEK
        // buffer is pinned and owned exclusively by CryptoService.
        try { _crypto?.Dispose(); } catch { /* swallow during exit */ }
        base.OnExit(e);
    }
}
