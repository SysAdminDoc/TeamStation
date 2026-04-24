using TeamStation.Core.Models;
using TeamStation.Launcher;
using TeamStation.Launcher.Validation;

namespace TeamStation.Tests;

/// <summary>
/// Pinned regression vectors for CVE-2020-13699 and related argv-injection
/// shapes. Each vector walks through every user-visible surface that could
/// relay the value to <c>TeamViewer.exe</c> or a URI handler — the validator,
/// the CLI argv builder (which calls back into the validator), and the URI
/// scheme builder (ditto). A regression that weakens any one of those paths
/// fails here before a code review.
/// </summary>
public class CveRegressionTests
{
    /// <summary>
    /// Exploit shapes published in, or directly derived from, the CVE-2020-13699
    /// advisory plus generic argv-injection shapes that landed in the hardening
    /// spec. Every one of these must be rejected by
    /// <see cref="LaunchInputValidator.ValidatePassword"/>.
    /// </summary>
    public static TheoryData<string, string> PasswordExploitVectors() => new()
    {
        { "unc_backslash_prefix",          @"\\attacker.example\share\payload" },
        { "unc_pipe_namespace",            @"\\.\pipe\evil" },
        { "unc_uncapitalised_mixed",       @"\\\\Attacker\\c$" },
        { "play_flag_leading",             "--play C:\\foo.tvs" },
        { "play_flag_leading_mixed_case",  "--Play C:\\foo.tvs" },
        { "play_flag_embedded",            "prefix--playsuffix" },
        { "control_flag_leading",          "--control 123 --password pw" },
        { "sendto_flag_leading",           "--Sendto foo" },
        { "id_flag_smuggled",              "--id 987654321" },
        { "api_token_flag_smuggled",       "--api-token deadbeef" },
        { "leading_dash",                  "-malicious" },
        { "path_traversal_relative_back",  @"..\..\windows\system32\cmd.exe" },
        { "path_traversal_relative_fwd",   "../../etc/passwd" },
        { "whitespace_argv_split_space",   "p w" },
        { "whitespace_argv_split_tab",     "p\tw" },
        { "whitespace_argv_split_cr",      "p\rw" },
        { "whitespace_argv_split_lf",      "p\nw" },
        { "null_byte_truncation",          "pass\0word" },
        { "single_backslash",              @"a\b" },
        { "forward_slash",                 "a/b" },
        { "colon_splitter",                "a:b" },
        { "double_quote",                  "a\"b" },
        { "single_quote",                  "a'b" },
        // Empty-string password is intentionally NOT a vector here: the
        // CLI and URI builders short-circuit on empty passwords and simply
        // skip the credential flag. That is the documented launch contract
        // (let TeamViewer prompt), not a bypass. Empty-string rejection at
        // the validator layer is pinned in LaunchInputValidatorTests.
    };

    [Theory]
    [MemberData(nameof(PasswordExploitVectors))]
    public void ValidatePassword_rejects_named_CVE_and_argv_injection_vector(string label, string payload)
    {
        _ = label; // documentation only — referenced in the failure message
        Assert.Throws<LaunchValidationException>(() => LaunchInputValidator.ValidatePassword(payload));
    }

    [Theory]
    [MemberData(nameof(PasswordExploitVectors))]
    public void UriSchemeBuilder_refuses_to_emit_URI_for_exploit_password(string label, string payload)
    {
        _ = label;
        var entry = new ConnectionEntry
        {
            Name = "target",
            TeamViewerId = "123456789",
            Password = payload,
            Mode = ConnectionMode.RemoteControl,
        };

        Assert.ThrowsAny<Exception>(() => UriSchemeBuilder.Build(entry));
    }

    [Theory]
    [MemberData(nameof(PasswordExploitVectors))]
    public void CliArgvBuilder_refuses_to_emit_argv_for_exploit_password(string label, string payload)
    {
        _ = label;
        var entry = new ConnectionEntry
        {
            Name = "target",
            TeamViewerId = "123456789",
            Password = payload,
            Mode = ConnectionMode.RemoteControl,
        };

        Assert.ThrowsAny<Exception>(() => CliArgvBuilder.Build(entry));
    }

    /// <summary>
    /// Device-ID shapes that would bypass the numeric-only regex if it
    /// regressed. All must be rejected.
    /// </summary>
    public static TheoryData<string, string> IdExploitVectors() => new()
    {
        { "arabic_indic_digits",     "١٢٣٤٥٦٧٨٩" },           // visually numeric, not ASCII
        { "fullwidth_digits",        "\uFF11\uFF12\uFF13\uFF14\uFF15\uFF16\uFF17\uFF18\uFF19" },
        { "zero_width_joiner",       "12345\u200D6789" },
        { "rtl_override",            "123456789\u202E" },
        { "whitespace_padding",      "  123456789  " },
        { "crlf_suffix",             "123456789\r\n" },
        { "play_smuggle_tail",       "123456789 --play foo" },
        { "play_smuggle_middle",     "123--play789" },
        { "scientific_notation",     "1.23e8" },
        { "negative_sign",           "-123456789" },
        { "hex_prefix",              "0x12345678" },
        { "unc_id",                  @"\\host\share" },
        { "empty",                   "" },
        { "too_short",               "1234567" },
        { "too_long",                "12345678901234" },
    };

    [Theory]
    [MemberData(nameof(IdExploitVectors))]
    public void ValidateTeamViewerId_rejects_named_exploit_shape(string label, string payload)
    {
        _ = label;
        Assert.Throws<LaunchValidationException>(() => LaunchInputValidator.ValidateTeamViewerId(payload));
    }

    [Theory]
    [MemberData(nameof(IdExploitVectors))]
    public void UriSchemeBuilder_refuses_to_emit_URI_for_exploit_id(string label, string payload)
    {
        _ = label;
        var entry = new ConnectionEntry { Name = "target", TeamViewerId = payload, Mode = ConnectionMode.RemoteControl };
        Assert.ThrowsAny<Exception>(() => UriSchemeBuilder.Build(entry));
    }

    [Theory]
    [MemberData(nameof(IdExploitVectors))]
    public void CliArgvBuilder_refuses_to_emit_argv_for_exploit_id(string label, string payload)
    {
        _ = label;
        var entry = new ConnectionEntry { Name = "target", TeamViewerId = payload, Mode = ConnectionMode.RemoteControl };
        Assert.ThrowsAny<Exception>(() => CliArgvBuilder.Build(entry));
    }

    public static TheoryData<string, string> ProxyEndpointExploitVectors() => new()
    {
        { "argv_flag_in_host",           "--ProxyIP 1.2.3.4:8080" },
        { "argv_flag_in_host_mixed",     "--proxYUser fake:8080" },
        { "play_flag_smuggle",           "--play foo:8080" },
        { "leading_dash",                "-ProxyPassword foo:8080" },
        { "empty",                       "" },
        { "whitespace_only",             "   " },
        { "port_overflow",               "host:99999" },
        { "port_zero",                   "host:0" },
        { "port_negative",               "host:-1" },
        { "unc_host",                    "\\\\unc:8080" },
        { "path_traversal_back",         @"..\..\windows\system32:8080" },
        { "path_traversal_fwd",          "../../etc/passwd:8080" },
        { "missing_host",                ":8080" },
        { "missing_port",                "host" },
        { "ipv4_with_space",             "10 .0.0.1:8080" },
        { "null_byte_in_host",           "host\0name:8080" },
    };

    [Theory]
    [MemberData(nameof(ProxyEndpointExploitVectors))]
    public void ValidateProxyEndpoint_rejects_argv_injection_via_proxy(string label, string endpoint)
    {
        _ = label;
        Assert.Throws<LaunchValidationException>(() => LaunchInputValidator.ValidateProxyEndpoint(endpoint));
    }

    /// <summary>
    /// End-to-end: a ConnectionEntry that carries a malicious proxy host,
    /// username, or password must be refused by <see cref="CliArgvBuilder"/>
    /// before any argv is emitted. Protects against regressions where a
    /// future builder change adds a new field but skips validation.
    /// </summary>
    [Theory]
    [InlineData("--ProxyIP 1.2.3.4:8080", "user", "pw")]
    [InlineData("host:80",                "--id", "pw")]
    [InlineData("host:80",                "user", "--play foo")]
    [InlineData("host:80",                "user", "\\\\attacker\\share")]
    [InlineData("host:80",                "-malicious-user", "pw")]
    public void CliArgvBuilder_rejects_proxy_injection_shapes(string endpoint, string username, string password)
    {
        var entry = new ConnectionEntry
        {
            Name = "via-proxy",
            TeamViewerId = "123456789",
            Mode = ConnectionMode.RemoteControl,
            Proxy = new ProxySettings(endpoint.Split(':', 2)[0], 80, username, password),
        };

        Assert.ThrowsAny<LaunchValidationException>(() => CliArgvBuilder.Build(entry));
    }

    /// <summary>
    /// The <c>teamviewer10://</c> scheme is the only one that accepts an
    /// <c>authorization</c> parameter on post-CVE-2020-13699 TeamViewer, but
    /// our builder preserves the same query shape for all six scheme variants.
    /// The validator must fire before any URI is produced — even for schemes
    /// where TeamViewer would ignore the param today.
    /// </summary>
    [Theory]
    [InlineData(ConnectionMode.RemoteControl)]
    [InlineData(ConnectionMode.FileTransfer)]
    [InlineData(ConnectionMode.Vpn)]
    [InlineData(ConnectionMode.Chat)]
    [InlineData(ConnectionMode.VideoCall)]
    [InlineData(ConnectionMode.Presentation)]
    public void UriSchemeBuilder_rejects_play_flag_in_authorization_for_every_scheme(ConnectionMode mode)
    {
        var entry = new ConnectionEntry
        {
            Name = "target",
            TeamViewerId = "123456789",
            Password = "--play foo",
            Mode = mode,
        };

        Assert.Throws<LaunchValidationException>(() => UriSchemeBuilder.Build(entry));
    }
}
