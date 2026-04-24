using System.Diagnostics;
using TeamStation.Core.Models;
using TeamStation.Launcher;

// TeamStation feasibility spike runner.
//
// Answers the two open questions captured in ROADMAP.md:
//   1) Does --PasswordB64 launch TeamViewer silently, or does it still prompt?
//   2) Do the six URI handlers (teamviewer10, tvfiletransfer1, tvchat1,
//      tvvpn1, tvvideocall1, tvpresent1) still honor ?authorization= on the
//      installed TV build?
//
// Usage:
//   TvLaunchSpike --id <TV_ID> --password <PW>
//   TvLaunchSpike --id <TV_ID> --password <PW> --cli-only
//   TvLaunchSpike --id <TV_ID> --password <PW> --uri-only
//
// Each test launches TeamViewer and pauses for the operator to confirm what
// was observed. Results are appended to spike-report.md next to the binary.

Console.OutputEncoding = System.Text.Encoding.UTF8;

var parsed = ParseArgs(args);
if (parsed is null)
{
    PrintUsage();
    return 2;
}

var (id, pw, cliOnly, uriOnly) = parsed.Value;

Console.WriteLine("TeamStation - TeamViewer launch feasibility spike");
Console.WriteLine($"  target ID      : {id}");
Console.WriteLine($"  password length: {pw.Length}");
Console.WriteLine();

var exe = TeamViewerPathResolver.Resolve();
if (exe is null)
{
    Console.Error.WriteLine("[FAIL] TeamViewer.exe not found. Install TeamViewer and retry.");
    return 3;
}
Console.WriteLine($"[ok] TeamViewer.exe resolved: {exe}");
Console.WriteLine($"     FileVersion: {FileVersionInfo.GetVersionInfo(exe).FileVersion}");
Console.WriteLine();

var report = new Report();

if (!uriOnly)
{
    RunSpike("CLI --PasswordB64 (Remote Control, default mode)",
        () => new TeamViewerLauncher().Launch(Entry(id, pw, ConnectionMode.RemoteControl),
            new LaunchOptions(UseBase64Password: true, ForceUri: false)), report);

    RunSpike("CLI --Password (plain, Remote Control, default mode)",
        () => new TeamViewerLauncher().Launch(Entry(id, pw, ConnectionMode.RemoteControl),
            new LaunchOptions(UseBase64Password: false, ForceUri: false)), report);

    RunSpike("CLI --mode fileTransfer",
        () => new TeamViewerLauncher().Launch(Entry(id, pw, ConnectionMode.FileTransfer)), report);

    RunSpike("CLI --mode vpn",
        () => new TeamViewerLauncher().Launch(Entry(id, pw, ConnectionMode.Vpn)), report);
}

if (!cliOnly)
{
    foreach (var mode in (ConnectionMode[])Enum.GetValues(typeof(ConnectionMode)))
    {
        RunSpike($"URI handler - {UriSchemeBuilder.SchemeFor(mode)}",
            () => new TeamViewerLauncher().Launch(Entry(id, pw, mode),
                new LaunchOptions(UseBase64Password: true, ForceUri: true)), report);
    }
}

WriteReport(report, exe);
Console.WriteLine();
Console.WriteLine($"[done] spike-report.md written to {Path.GetFullPath("spike-report.md")}");
return 0;

static ConnectionEntry Entry(string id, string pw, ConnectionMode mode) => new()
{
    Name = $"Spike - {mode}",
    TeamViewerId = id,
    Password = pw,
    Mode = mode,
};

static void RunSpike(string name, Func<LaunchOutcome> run, Report report)
{
    Console.WriteLine(new string('-', 72));
    Console.WriteLine($"> {name}");
    Console.WriteLine(new string('-', 72));

    LaunchOutcome outcome;
    try { outcome = run(); }
    catch (Exception ex)
    {
        Console.WriteLine($"  [exception] {ex.Message}");
        report.Add(new SpikeResult(name, Launched: false, OperatorNotes: $"exception: {ex.Message}", Argv: null, Uri: null));
        return;
    }

    if (!outcome.Success)
    {
        Console.WriteLine($"  [not launched] {outcome.Error}");
        report.Add(new SpikeResult(name, Launched: false, OperatorNotes: outcome.Error ?? "unknown", Argv: null, Uri: null));
        return;
    }

    Console.WriteLine($"  pid     : {outcome.ProcessId}");
    if (outcome.Argv.Count > 0)
    {
        Console.WriteLine($"  argv    : [{string.Join(", ", RedactArgv(outcome.Argv))}]");
    }
    if (outcome.Uri is not null)
    {
        Console.WriteLine($"  uri     : {RedactAuth(outcome.Uri)}");
    }

    Console.WriteLine();
    Console.WriteLine("Observe the TeamViewer window. Pick the closest match:");
    Console.WriteLine("  [1] silent connect attempt, no password prompt");
    Console.WriteLine("  [2] connection dialog appeared asking for password");
    Console.WriteLine("  [3] TeamViewer opened but did nothing (params ignored)");
    Console.WriteLine("  [4] TeamViewer rejected / crashed / error popup");
    Console.WriteLine("  [5] other - will be recorded verbatim");
    Console.Write("choice: ");
    var choice = Console.ReadLine()?.Trim();

    var notes = choice switch
    {
        "1" => "silent connect attempt, no password prompt",
        "2" => "password dialog appeared",
        "3" => "TeamViewer opened but params ignored",
        "4" => "rejected / crashed / error popup",
        _ => AskFreeform(),
    };

    var argvSurface = outcome.Argv.Count > 0
        ? string.Join(' ', RedactArgv(outcome.Argv).Select(Quote))
        : null;
    report.Add(new SpikeResult(name, Launched: true, OperatorNotes: notes,
        Argv: argvSurface,
        Uri: outcome.Uri is null ? null : RedactAuth(outcome.Uri)));

    Console.WriteLine("  recorded.");
    Console.WriteLine();
}

static IEnumerable<string> RedactArgv(IReadOnlyList<string> argv)
{
    for (var i = 0; i < argv.Count; i++)
    {
        if (i > 0 && argv[i - 1] is "--PasswordB64" or "--Password" or "--ProxyPassword")
            yield return "<redacted>";
        else
            yield return argv[i];
    }
}

static string AskFreeform()
{
    Console.Write("  describe: ");
    return Console.ReadLine() ?? string.Empty;
}

static string RedactAuth(string uri)
{
    var idx = uri.IndexOf("authorization=", StringComparison.OrdinalIgnoreCase);
    if (idx < 0) return uri;
    var end = uri.IndexOf('&', idx);
    return end < 0
        ? uri[..(idx + "authorization=".Length)] + "<redacted>"
        : uri[..(idx + "authorization=".Length)] + "<redacted>" + uri[end..];
}

static string Quote(string s) => s.Contains(' ') ? $"\"{s}\"" : s;

static void WriteReport(Report report, string exe)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("# TeamStation launch feasibility - spike report");
    sb.AppendLine();
    sb.AppendLine($"- Run at: {DateTimeOffset.Now:O}");
    sb.AppendLine($"- TeamViewer.exe: `{exe}`");
    sb.AppendLine($"- FileVersion: `{FileVersionInfo.GetVersionInfo(exe).FileVersion}`");
    sb.AppendLine();
    sb.AppendLine("| Test | Launched | Operator notes | argv / URI |");
    sb.AppendLine("|---|---|---|---|");
    foreach (var r in report.Results)
    {
        var surface = r.Argv ?? r.Uri ?? "-";
        sb.AppendLine($"| {r.Name} | {(r.Launched ? "yes" : "no")} | {r.OperatorNotes.Replace("|", "\\|")} | `{surface.Replace("|", "\\|")}` |");
    }
    File.WriteAllText("spike-report.md", sb.ToString());
}

static (string id, string pw, bool cliOnly, bool uriOnly)? ParseArgs(string[] a)
{
    string? id = null, pw = null;
    bool cliOnly = false, uriOnly = false;
    for (var i = 0; i < a.Length; i++)
    {
        switch (a[i])
        {
            case "--id" when i + 1 < a.Length: id = a[++i]; break;
            case "--password" when i + 1 < a.Length: pw = a[++i]; break;
            case "--cli-only": cliOnly = true; break;
            case "--uri-only": uriOnly = true; break;
            default: return null;
        }
    }
    if (id is null || pw is null) return null;
    return (id, pw, cliOnly, uriOnly);
}

static void PrintUsage()
{
    Console.Error.WriteLine("usage: TvLaunchSpike --id <TV_ID> --password <PW> [--cli-only | --uri-only]");
}

sealed class Report
{
    public List<SpikeResult> Results { get; } = new();
    public void Add(SpikeResult r) => Results.Add(r);
}

sealed record SpikeResult(string Name, bool Launched, string OperatorNotes, string? Argv, string? Uri);
