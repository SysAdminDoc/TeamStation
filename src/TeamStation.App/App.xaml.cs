using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using TeamStation.App.Services;
using TeamStation.App.ViewModels;
using TeamStation.App.Views;
using TeamStation.Core.Models;
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
        // CLI sub-commands — must be checked before the mutex and before any UI is
        // created so the process can be driven from PowerShell or cmd headlessly.
        if (Array.IndexOf(e.Args, "--verify-audit-chain") >= 0)
        {
            RunAuditChainVerificationCli();
            return;
        }
        if (Array.IndexOf(e.Args, "--export-audit-log") >= 0)
        {
            RunAuditExportCli(e.Args);
            return;
        }

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
                SlowQueryThreshold = TimeSpan.FromMilliseconds(
                    AppSettings.NormalizeSlowQueryThresholdMs(settings.SlowQueryThresholdMs)),
            };
            var startupIntegrity = db.CheckIntegrity();
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
            var audit = new AuditLogRepository(db, crypto);
            var launcher = new TeamViewerLauncher(() => ResolveTeamViewerPath(settings));
            var tvExePath = ResolveTeamViewerPath(settings);
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? "dev";

            // DEK rotation delegate — available in DPAPI (standard) mode only.
            // Portable mode uses a master-password-wrapped DEK; that path needs
            // a separate re-prompt flow that is not yet implemented.
            Func<(int entries, int folders)>? rotateDek = null;
            if (!StoragePaths.IsPortable(out _))
            {
                rotateDek = () =>
                {
                    var (entryCount, folderCount) = (0, 0);
                    var newSvc = CryptoService.RotateDek(db, (oldSvc, newSvc) =>
                    {
                        using var c = db.OpenConnection();
                        using var tx = c.BeginTransaction();

                        // Re-read and re-encrypt all entry password fields.
                        var entryRows = new List<(string id, byte[]? pw, byte[]? proxyPw)>();
                        using (var cmd = c.CreateCommand())
                        {
                            cmd.Transaction = tx;
                            cmd.CommandText = "SELECT id, password_enc, proxy_pass_enc FROM entries;";
                            using var reader = cmd.ExecuteReader();
                            while (reader.Read())
                            {
                                var id = reader.GetString(0);
                                var pw = reader.IsDBNull(1) ? null : (byte[])reader[1];
                                var proxyPw = reader.IsDBNull(2) ? null : (byte[])reader[2];
                                entryRows.Add((id, pw, proxyPw));
                            }
                        }

                        foreach (var (id, pw, proxyPw) in entryRows)
                        {
                            var newPw = pw is null ? null : newSvc.EncryptBytes(oldSvc.DecryptToBytes(pw));
                            var newProxyPw = proxyPw is null ? null : newSvc.EncryptBytes(oldSvc.DecryptToBytes(proxyPw));
                            using var cmd = c.CreateCommand();
                            cmd.Transaction = tx;
                            cmd.CommandText = "UPDATE entries SET password_enc=$pw, proxy_pass_enc=$proxy WHERE id=$id;";
                            cmd.Parameters.AddWithValue("$pw", (object?)newPw ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("$proxy", (object?)newProxyPw ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("$id", id);
                            cmd.ExecuteNonQuery();
                            if (pw is not null || proxyPw is not null) entryCount++;
                        }

                        // Re-read and re-encrypt all folder default password fields.
                        var folderRows = new List<(string id, byte[] pw)>();
                        using (var cmd2 = c.CreateCommand())
                        {
                            cmd2.Transaction = tx;
                            cmd2.CommandText = "SELECT id, default_password_enc FROM folders WHERE default_password_enc IS NOT NULL;";
                            using var reader = cmd2.ExecuteReader();
                            while (reader.Read())
                                folderRows.Add((reader.GetString(0), (byte[])reader[1]));
                        }

                        foreach (var (id, pw) in folderRows)
                        {
                            var newPw = newSvc.EncryptBytes(oldSvc.DecryptToBytes(pw))!;
                            using var cmd = c.CreateCommand();
                            cmd.Transaction = tx;
                            cmd.CommandText = "UPDATE folders SET default_password_enc=$pw WHERE id=$id;";
                            cmd.Parameters.AddWithValue("$pw", newPw);
                            cmd.Parameters.AddWithValue("$id", id);
                            cmd.ExecuteNonQuery();
                            folderCount++;
                        }

                        tx.Commit();
                    });

                    entries.UpdateCrypto(newSvc);
                    folders.UpdateCrypto(newSvc);
                    _crypto = newSvc;

                    audit.Append(new AuditEvent
                    {
                        Action = "rotate-dek",
                        TargetType = "vault",
                        Summary = $"Encryption key rotated. Re-encrypted {entryCount} connection(s) and {folderCount} folder default(s).",
                    });

                    return (entryCount, folderCount);
                };
            }

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
                startupDbPath: dbPath,
                startupIntegrityReport: startupIntegrity,
                rotateDek: rotateDek);

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

    // -------------------------------------------------------------------------
    // CLI: --verify-audit-chain
    // -------------------------------------------------------------------------

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int dwProcessId);

    /// <summary>
    /// Headless path for <c>TeamStation.exe --verify-audit-chain</c>.
    /// Attaches to the parent process's console so output appears inline in
    /// PowerShell/cmd, initializes the DB + crypto, runs
    /// <see cref="AuditLogRepository.VerifyChain"/>, prints a one-line result,
    /// then exits with code 0 (PASS) or 1 (FAIL).
    /// </summary>
    private void RunAuditChainVerificationCli()
    {
        const int attachParentProcess = -1;
        AttachConsole(attachParentProcess);

        // WinExe severs the standard streams on start-up; reattach them so
        // Console.WriteLine actually reaches the parent terminal.
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        Console.WriteLine(); // separate our output from any parent shell prompt

        try
        {
            var dbPath = StoragePaths.ResolveDatabasePath();
            var db = new Database(dbPath);
            var crypto = CreateCrypto(db);
            var audit = new AuditLogRepository(db, crypto);
            var result = audit.VerifyChain();
            Console.WriteLine(result.Summary);
            Shutdown(result.IsValid ? 0 : 1);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Verification error: {ex.GetType().Name}: {ex.Message}");
            Shutdown(2);
        }
    }

    // -------------------------------------------------------------------------
    // CLI: --export-audit-log
    // -------------------------------------------------------------------------

    /// <summary>
    /// Headless path for <c>TeamStation.exe --export-audit-log</c>.
    /// <para>
    /// Flags (all optional):
    /// <list type="bullet">
    ///   <item><c>--format=ndjson</c> (default) or <c>--format=csv</c></item>
    ///   <item><c>--output=&lt;path&gt;</c> — file to write; omit to write to stdout</item>
    ///   <item><c>--skip-verify</c> — skip HMAC chain verification before export</item>
    /// </list>
    /// Exit codes: 0 = success, 1 = chain verification failed, 2 = error.
    /// </para>
    /// </summary>
    private void RunAuditExportCli(string[] args)
    {
        const int attachParentProcess = -1;
        AttachConsole(attachParentProcess);
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        Console.WriteLine();

        try
        {
            var format = GetArgValue(args, "--format=") ?? "ndjson";
            var outputPath = GetArgValue(args, "--output=");
            var skipVerify = Array.IndexOf(args, "--skip-verify") >= 0;

            var dbPath = StoragePaths.ResolveDatabasePath();
            var db = new Database(dbPath);
            var crypto = CreateCrypto(db);
            var audit = new AuditLogRepository(db, crypto);

            if (!skipVerify)
            {
                var chainResult = audit.VerifyChain();
                Console.Error.WriteLine($"Chain: {chainResult.Summary}");
                if (!chainResult.IsValid)
                {
                    Shutdown(1);
                    return;
                }
            }

            var events = audit.GetAll();

            TextWriter? fileWriter = null;
            try
            {
                var writer = outputPath is not null
                    ? fileWriter = new StreamWriter(outputPath, append: false, System.Text.Encoding.UTF8)
                    : Console.Out;

                if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
                    TeamStation.Data.Storage.AuditLogExporter.WriteCsv(events, writer);
                else
                    TeamStation.Data.Storage.AuditLogExporter.WriteNdjson(events, writer);

                writer.Flush();
            }
            finally
            {
                fileWriter?.Dispose();
            }

            if (outputPath is not null)
                Console.Error.WriteLine($"Exported {events.Count} row(s) [{format}] → {outputPath}");

            Shutdown(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Export error: {ex.GetType().Name}: {ex.Message}");
            Shutdown(2);
        }
    }

    private static string? GetArgValue(string[] args, string prefix)
    {
        foreach (var a in args)
            if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return a[prefix.Length..];
        return null;
    }

    protected override void OnExit(ExitEventArgs e)    {
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
