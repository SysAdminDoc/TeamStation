using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using TeamStation.Core.Models;

namespace TeamStation.Core.Services;

public static partial class ExternalToolRunner
{
    public static Process? Run(ExternalToolDefinition tool, ConnectionEntry entry)
    {
        return Process.Start(CreateToolStartInfo(tool, entry));
    }

    public static ProcessStartInfo CreateToolStartInfo(ExternalToolDefinition tool, ConnectionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(entry);

        var psi = new ProcessStartInfo
        {
            FileName = NormalizeCommandFileName(Expand(tool.Command, entry)),
            UseShellExecute = false,
        };

        foreach (var arg in SplitArguments(Expand(tool.Arguments, entry)))
            psi.ArgumentList.Add(arg);

        return psi;
    }

    public static void RunScript(string? script, ConnectionEntry entry)
    {
        var psi = CreateScriptStartInfo(script, entry);
        if (psi is null)
            return;

        Process.Start(psi);
    }

    public static ProcessStartInfo? CreateScriptStartInfo(string? script, ConnectionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(script))
            return null;

        var expanded = Expand(script, entry);
        if (string.IsNullOrWhiteSpace(expanded))
            return null;

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(expanded);
        return psi;
    }

    public static string Expand(string? value, ConnectionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (value is null)
            return string.Empty;

        var result = value
            .Replace("%ID%", entry.TeamViewerId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("%NAME%", entry.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("%PASSWORD%", entry.Password ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("%PROFILE%", entry.ProfileName ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        result = TagRegex().Replace(result, match =>
        {
            var key = match.Groups[1].Value.Trim();
            if (key.Length == 0 || key.Contains('='))
                return string.Empty;

            var prefix = $"{key}=";
            return (entry.Tags ?? [])
                .FirstOrDefault(t => t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..] ?? string.Empty;
        });

        result = EnvRegex().Replace(result, match =>
            Environment.GetEnvironmentVariable(match.Groups[1].Value) ?? string.Empty);

        return result;
    }

    public static IReadOnlyList<string> SplitArguments(string? value)
    {
        var arguments = new List<string>();
        if (string.IsNullOrWhiteSpace(value))
            return arguments;

        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '\\' && i + 1 < value.Length && value[i + 1] == '"')
            {
                current.Append('"');
                hasToken = true;
                i++;
                continue;
            }

            if (ch == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (hasToken)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }

                continue;
            }

            current.Append(ch);
            hasToken = true;
        }

        if (inQuotes)
            throw new InvalidOperationException("External tool arguments contain an unterminated quoted value.");

        if (hasToken)
            arguments.Add(current.ToString());

        return arguments;
    }

    private static string NormalizeCommandFileName(string value)
    {
        var command = value.Trim();
        if (command.Length == 0)
            throw new InvalidOperationException("External tool command is empty.");

        if (command[0] != '"')
            return command;

        var fileName = new StringBuilder();
        for (var i = 1; i < command.Length; i++)
        {
            var ch = command[i];
            if (ch == '\\' && i + 1 < command.Length && command[i + 1] == '"')
            {
                fileName.Append('"');
                i++;
                continue;
            }

            if (ch == '"')
            {
                if (!string.IsNullOrWhiteSpace(command[(i + 1)..]))
                    throw new InvalidOperationException("External tool command must contain only the executable path. Put arguments in the Arguments field.");

                var normalized = fileName.ToString();
                if (string.IsNullOrWhiteSpace(normalized))
                    throw new InvalidOperationException("External tool command is empty.");

                return normalized;
            }

            fileName.Append(ch);
        }

        throw new InvalidOperationException("External tool command contains an unterminated quoted path.");
    }

    [GeneratedRegex("%TAG:([^%]+)%", RegexOptions.IgnoreCase)]
    private static partial Regex TagRegex();

    [GeneratedRegex("\\$\\{([^}]+)\\}")]
    private static partial Regex EnvRegex();
}
