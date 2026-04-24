using System.Diagnostics;
using System.Text.RegularExpressions;
using TeamStation.Core.Models;

namespace TeamStation.App.Services;

public static partial class ExternalToolRunner
{
    public static Process? Run(ExternalToolDefinition tool, ConnectionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(tool.Command))
            throw new InvalidOperationException("External tool command is empty.");

        var psi = new ProcessStartInfo
        {
            FileName = Expand(tool.Command, entry),
            UseShellExecute = false,
        };

        foreach (var arg in SplitArguments(Expand(tool.Arguments, entry)))
            psi.ArgumentList.Add(arg);

        return Process.Start(psi);
    }

    public static void RunScript(string script, ConnectionEntry entry)
    {
        if (string.IsNullOrWhiteSpace(script))
            return;

        var expanded = Expand(script, entry);
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(expanded);
        Process.Start(psi);
    }

    public static string Expand(string value, ConnectionEntry entry)
    {
        var result = value
            .Replace("%ID%", entry.TeamViewerId, StringComparison.OrdinalIgnoreCase)
            .Replace("%NAME%", entry.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("%PASSWORD%", entry.Password ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("%PROFILE%", entry.ProfileName, StringComparison.OrdinalIgnoreCase);

        result = TagRegex().Replace(result, match =>
        {
            var key = match.Groups[1].Value;
            var prefix = $"{key}=";
            return entry.Tags.FirstOrDefault(t => t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..] ?? string.Empty;
        });

        result = EnvRegex().Replace(result, match =>
            Environment.GetEnvironmentVariable(match.Groups[1].Value) ?? string.Empty);

        return result;
    }

    private static IEnumerable<string> SplitArguments(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        foreach (Match match in ArgumentRegex().Matches(value))
            yield return match.Groups["quoted"].Success
                ? match.Groups["quoted"].Value
                : match.Groups["bare"].Value;
    }

    [GeneratedRegex("%TAG:([^%]+)%", RegexOptions.IgnoreCase)]
    private static partial Regex TagRegex();

    [GeneratedRegex("\\$\\{([^}]+)\\}")]
    private static partial Regex EnvRegex();

    [GeneratedRegex("\"(?<quoted>[^\"]*)\"|(?<bare>\\S+)")]
    private static partial Regex ArgumentRegex();
}
