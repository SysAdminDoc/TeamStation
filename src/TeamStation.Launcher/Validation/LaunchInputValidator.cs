using System.Text.RegularExpressions;

namespace TeamStation.Launcher.Validation;

/// <summary>
/// Hardening layer against CVE-2020-13699 and general argv-injection risks.
/// Every user-supplied value reaches <c>Process.Start</c> or the URI handler
/// through one of these predicates. Failures surface as <see cref="LaunchValidationException"/>.
/// </summary>
public static partial class LaunchInputValidator
{
    public const int MaxIdLength = 12;
    public const int MaxPasswordLength = 256;
    public const int MaxProxyUserLength = 128;

    // ASCII-only digit class. .NET's `\d` matches any Unicode Nd category
    // character (Arabic-Indic, full-width, Bengali, etc.), which would let
    // a device ID written in non-ASCII digits slip past the regex even
    // though the TeamViewer CLI only accepts ASCII decimal IDs.
    [GeneratedRegex(@"^[0-9]{8,12}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    private static readonly char[] ForbiddenInPassword =
    [
        '\\', '/', ':', '\0', '\r', '\n', '\t', ' ', '"', '\''
    ];

    private static readonly string[] ForbiddenSubstringsInAny =
    [
        "--play", "--control", "--Sendto", "--id", "--api-token",
        "\\\\", "..\\", "../"
    ];

    public static void ValidateTeamViewerId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new LaunchValidationException("TeamViewer ID is required.");
        if (id.Length > MaxIdLength)
            throw new LaunchValidationException($"TeamViewer ID exceeds {MaxIdLength} characters.");
        if (!IdPattern().IsMatch(id))
            throw new LaunchValidationException("TeamViewer ID must be 8-12 digits.");
    }

    public static void ValidatePassword(string password)
    {
        if (password is null)
            throw new LaunchValidationException("Password is required.");
        if (password.Length == 0)
            throw new LaunchValidationException("Password must not be empty.");
        if (password.Length > MaxPasswordLength)
            throw new LaunchValidationException($"Password exceeds {MaxPasswordLength} characters.");
        if (password.StartsWith('-'))
            throw new LaunchValidationException("Password must not start with '-'.");
        if (password.IndexOfAny(ForbiddenInPassword) >= 0)
            throw new LaunchValidationException("Password contains a forbidden character.");
        foreach (var bad in ForbiddenSubstringsInAny)
        {
            if (password.Contains(bad, StringComparison.OrdinalIgnoreCase))
                throw new LaunchValidationException($"Password contains forbidden substring '{bad}'.");
        }
    }

    public static void ValidateProxyEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new LaunchValidationException("Proxy endpoint is required.");
        var parts = endpoint.Split(':');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) ||
            !int.TryParse(parts[1], out var port) || port is < 1 or > 65535)
        {
            throw new LaunchValidationException("Proxy endpoint must be host:port with a port in 1-65535.");
        }

        // Host must not smuggle argv injection shapes: whitespace (splits
        // the field into flags), leading dash (argv-flag shape), UNC
        // prefix, or any of the substrings we already ban in passwords.
        var host = parts[0];
        if (host.StartsWith('-'))
            throw new LaunchValidationException("Proxy host must not start with '-'.");
        if (host.IndexOfAny(ForbiddenInPassword) >= 0)
            throw new LaunchValidationException("Proxy host contains a forbidden character.");
        foreach (var bad in ForbiddenSubstringsInAny)
        {
            if (host.Contains(bad, StringComparison.OrdinalIgnoreCase))
                throw new LaunchValidationException($"Proxy host contains forbidden substring '{bad}'.");
        }
    }

    public static void ValidateProxyUsername(string username)
    {
        if (string.IsNullOrEmpty(username)) return;
        if (username.Length > MaxProxyUserLength)
            throw new LaunchValidationException($"Proxy username exceeds {MaxProxyUserLength} characters.");
        if (username.StartsWith('-'))
            throw new LaunchValidationException("Proxy username must not start with '-'.");
        if (username.IndexOfAny(ForbiddenInPassword) >= 0)
            throw new LaunchValidationException("Proxy username contains a forbidden character.");
    }
}

public sealed class LaunchValidationException : Exception
{
    public LaunchValidationException(string message) : base(message) { }
}
