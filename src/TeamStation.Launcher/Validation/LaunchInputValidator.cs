using System.Buffers;
using System.Collections.Frozen;
using TeamStation.Core.Models;

namespace TeamStation.Launcher.Validation;

/// <summary>
/// Hardening layer against CVE-2020-13699 and general argv-injection risks.
/// Every user-supplied value reaches <c>Process.Start</c> or the URI handler
/// through one of these predicates. Failures surface as <see cref="LaunchValidationException"/>.
/// </summary>
public static partial class LaunchInputValidator
{
    public const int MaxIdLength = TeamViewerIdFormat.MaxLength;
    public const int MaxPasswordLength = 256;
    public const int MaxProxyUserLength = 128;

    private static readonly SearchValues<char> ForbiddenInPassword =
        SearchValues.Create(['\\', '/', ':', '\0', '\r', '\n', '\t', ' ', '"', '\'']);

    private static readonly FrozenSet<string> ForbiddenSubstringsInAny =
        new string[]
        {
            "--play", "--control", "--Sendto", "--id", "--api-token",
            "\\\\", "..\\", "../"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static void ValidateTeamViewerId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new LaunchValidationException("TeamViewer ID is required.");
        if (id.Length > MaxIdLength)
            throw new LaunchValidationException($"TeamViewer ID exceeds {MaxIdLength} characters.");
        if (!TeamViewerIdFormat.IsValid(id))
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
        if (password.AsSpan().IndexOfAny(ForbiddenInPassword) >= 0)
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

        // Bracket-form for IPv6 literals: [::1]:8080 / [fe80::1%eth0]:3128.
        // Non-bracket form for IPv4 + hostnames: 10.0.0.1:8080 / proxy.internal:3128.
        // A bare IPv6 address (no brackets) is rejected so the port split
        // stays unambiguous.
        string host;
        string portStr;
        bool isBracketedIpv6 = false;
        if (endpoint.StartsWith('['))
        {
            var close = endpoint.IndexOf(']');
            if (close < 0 || close >= endpoint.Length - 1 || endpoint[close + 1] != ':')
                throw new LaunchValidationException("Bracketed proxy endpoint must be [host]:port.");
            host = endpoint[1..close];
            portStr = endpoint[(close + 2)..];
            isBracketedIpv6 = true;
            if (host.Length == 0)
                throw new LaunchValidationException("Proxy endpoint must have a host.");
        }
        else
        {
            var lastColon = endpoint.LastIndexOf(':');
            if (lastColon <= 0 || lastColon >= endpoint.Length - 1)
                throw new LaunchValidationException("Proxy endpoint must be host:port with a port in 1-65535.");
            host = endpoint[..lastColon];
            portStr = endpoint[(lastColon + 1)..];
            // A non-bracketed host containing ':' is an unquoted IPv6
            // literal. Refuse so the port split stays deterministic and
            // the argv-injection guard below isn't undermined by stray
            // colons smuggled into the host part.
            if (host.Contains(':'))
                throw new LaunchValidationException("IPv6 proxy hosts must be wrapped in brackets: [host]:port.");
        }

        if (string.IsNullOrWhiteSpace(host))
            throw new LaunchValidationException("Proxy endpoint must have a host.");
        if (!int.TryParse(portStr, System.Globalization.CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
            throw new LaunchValidationException("Proxy endpoint must be host:port with a port in 1-65535.");

        // Host argv-injection guards. For IPv6 literals, `:` is part of
        // the address syntax, so it's the one forbidden character we
        // allow; every other char in ForbiddenInPassword is still banned.
        if (host.StartsWith('-'))
            throw new LaunchValidationException("Proxy host must not start with '-'.");
        foreach (var ch in ForbiddenInPassword)
        {
            if (ch == ':' && isBracketedIpv6) continue;
            if (host.IndexOf(ch) >= 0)
                throw new LaunchValidationException("Proxy host contains a forbidden character.");
        }
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
