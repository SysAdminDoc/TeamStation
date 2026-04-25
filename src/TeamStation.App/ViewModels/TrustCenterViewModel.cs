using System.IO;
using System.Runtime.Versioning;
using TeamStation.App.Mvvm;
using TeamStation.App.Services;
using TeamStation.Data.Storage;
using TeamStation.Launcher;

namespace TeamStation.App.ViewModels;

/// <summary>
/// View-model for the Trust Center dialog. Owns a <see cref="TrustCenterReport"/>
/// produced by <see cref="TrustCenterReportFactory.Build"/> from probes
/// against the live system. Exposes a <see cref="RefreshCommand"/> so the
/// operator can re-probe without closing the dialog.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TrustCenterViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly string _databasePath;
    private TrustCenterReport _report;

    public TrustCenterViewModel(AppSettings settings, string databasePath)
    {
        _settings = settings;
        _databasePath = databasePath;
        RefreshCommand = new RelayCommand(Refresh);
        _report = ProbeAndBuild();
    }

    public TrustCenterReport Report
    {
        get => _report;
        private set => SetField(ref _report, value);
    }

    public System.Windows.Input.ICommand RefreshCommand { get; }

    public void Refresh() => Report = ProbeAndBuild();

    private TrustCenterReport ProbeAndBuild()
    {
        var detected = TeamViewerVersionDetector.Detect();
        var safety = TeamViewerVersionDetector.EvaluateSafety(detected);
        var registry = TeamViewerCveRegistry.Default;

        TeamViewerBinaryProvenance provenance;
        try
        {
            provenance = TeamViewerBinaryProvenanceInspector.Inspect(_settings.TeamViewerPathOverride);
        }
        catch
        {
            // Provenance inspection is advisory — never fail the dialog.
            provenance = TeamViewerBinaryProvenanceEvaluator.Evaluate(
                resolvedPath: _settings.TeamViewerPathOverride,
                fileExists: false,
                signatureState: TeamViewerSignatureState.UnableToVerify,
                publisherSubject: null,
                fileVersion: null);
        }

        long? dbSize = null;
        DateTimeOffset? dbLastWrite = null;
        if (File.Exists(_databasePath))
        {
            try
            {
                var info = new FileInfo(_databasePath);
                dbSize = info.Length;
                dbLastWrite = new DateTimeOffset(info.LastWriteTime);
            }
            catch
            {
                // Best-effort — leave the size + age unset rather than throw.
            }
        }

        var portableMode = StoragePaths.IsPortable(out _);

        string? mirrorFile = null;
        DateTimeOffset? mirrorLastWrite = null;
        var cloudFolder = string.IsNullOrWhiteSpace(_settings.CloudSyncFolder) ? null : _settings.CloudSyncFolder;
        if (cloudFolder is not null)
        {
            try
            {
                var candidate = Path.Combine(cloudFolder, Path.GetFileName(_databasePath));
                if (File.Exists(candidate))
                {
                    mirrorFile = candidate;
                    mirrorLastWrite = new DateTimeOffset(File.GetLastWriteTime(candidate));
                }
            }
            catch
            {
                // Folder may have been removed between Settings save and now.
            }
        }

        var hasToken = !string.IsNullOrWhiteSpace(_settings.TeamViewerApiToken);

        return TrustCenterReportFactory.Build(
            now: DateTimeOffset.Now,
            safetyStatus: safety,
            provenance: provenance,
            registry: registry,
            databasePath: _databasePath,
            databaseSizeBytes: dbSize,
            databaseLastWrite: dbLastWrite,
            portableMode: portableMode,
            cloudSyncFolder: cloudFolder,
            mirrorFile: mirrorFile,
            mirrorLastWrite: mirrorLastWrite,
            webApiTokenConfigured: hasToken);
    }
}
