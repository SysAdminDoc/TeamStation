using TeamStation.Launcher.Validation;

namespace TeamStation.Tests;

public class LaunchInputValidatorTests
{
    [Theory]
    [InlineData("12345678")]       // 8-digit lower bound
    [InlineData("123456789")]      // 9-digit (most common TV ID length)
    [InlineData("123456789012")]   // 12-digit upper bound
    public void ValidateTeamViewerId_accepts_numeric_ids_in_range(string id)
    {
        LaunchInputValidator.ValidateTeamViewerId(id);
    }

    [Theory]
    [InlineData("")]                   // empty
    [InlineData("    ")]                // whitespace
    [InlineData("1234567")]             // 7 digits — too short
    [InlineData("1234567890123")]       // 13 digits — too long
    [InlineData("12345abc9")]           // non-digits
    [InlineData("123 456 789")]         // embedded space
    [InlineData("123-456-789")]         // hyphenated
    [InlineData("123456789\r\n")]       // trailing CRLF
    public void ValidateTeamViewerId_rejects_invalid_ids(string id)
    {
        Assert.Throws<LaunchValidationException>(() => LaunchInputValidator.ValidateTeamViewerId(id));
    }

    [Theory]
    [InlineData("normal-password")]
    [InlineData("P@ssw0rd!#$%^&*()")]
    [InlineData("unicode ☃ — snowman")] // space is forbidden, skip this
    [InlineData("1234")]
    public void ValidatePassword_handles_well_formed_passwords(string pw)
    {
        // Space is forbidden; skip that specific case.
        if (pw.Contains(' '))
        {
            Assert.Throws<LaunchValidationException>(() => LaunchInputValidator.ValidatePassword(pw));
            return;
        }
        LaunchInputValidator.ValidatePassword(pw);
    }

    [Theory]
    [InlineData("")]                             // empty
    [InlineData("-leading-dash")]                 // argv-injection shape
    [InlineData("contains\nnewline")]             // newline
    [InlineData("contains\ttab")]                 // tab
    [InlineData("back\\slash")]                   // CVE-2020-13699 shape
    [InlineData("forward/slash")]                 // alt CVE shape
    [InlineData("has --play in middle")]          // space + forbidden substring
    [InlineData("evil\\\\unc")]                   // UNC prefix
    [InlineData("..\\relative")]                  // relative traversal
    public void ValidatePassword_rejects_dangerous_passwords(string pw)
    {
        Assert.Throws<LaunchValidationException>(() => LaunchInputValidator.ValidatePassword(pw));
    }

    [Fact]
    public void ValidatePassword_rejects_play_substring_even_nested()
    {
        Assert.Throws<LaunchValidationException>(
            () => LaunchInputValidator.ValidatePassword("prefix--playsuffix"));
    }

    [Fact]
    public void ValidatePassword_rejects_at_max_length_plus_one()
    {
        var pw = new string('a', LaunchInputValidator.MaxPasswordLength + 1);
        Assert.Throws<LaunchValidationException>(() => LaunchInputValidator.ValidatePassword(pw));
    }

    [Theory]
    [InlineData("10.0.0.1:8080")]
    [InlineData("proxy.internal:3128")]
    [InlineData("[::1]:8080")]                              // IPv6 loopback
    [InlineData("[2001:db8::1]:3128")]                      // full IPv6
    [InlineData("[fe80::1]:80")]                            // IPv6 link-local
    public void ValidateProxyEndpoint_accepts_valid_endpoints(string endpoint)
    {
        LaunchInputValidator.ValidateProxyEndpoint(endpoint);
    }

    [Theory]
    [InlineData("::1:8080")]                                // bare IPv6, no brackets
    [InlineData("[::1]")]                                   // missing :port
    [InlineData("[::1]8080")]                               // missing colon between ] and port
    [InlineData("[]:8080")]                                 // empty host inside brackets
    [InlineData("[::1]:-1")]                                // negative port
    [InlineData("[::1]:0")]                                 // port zero
    [InlineData("[::1]:99999")]                             // port overflow
    [InlineData("[-malicious]:8080")]                       // leading-dash host inside brackets
    [InlineData("[host\"name]:8080")]                       // double quote inside brackets
    [InlineData("[host'name]:8080")]                        // single quote inside brackets
    [InlineData("[host name]:8080")]                        // whitespace inside brackets
    [InlineData("[host\\name]:8080")]                       // backslash inside brackets
    [InlineData("[host/name]:8080")]                        // forward slash inside brackets
    [InlineData("[host\0name]:8080")]                       // null byte inside brackets
    [InlineData("[host--play]:8080")]                       // forbidden substring inside brackets
    [InlineData("[\\\\unc]:8080")]                          // UNC prefix inside brackets
    public void ValidateProxyEndpoint_rejects_malformed_ipv6(string endpoint)
    {
        Assert.Throws<LaunchValidationException>(
            () => LaunchInputValidator.ValidateProxyEndpoint(endpoint));
    }

    /// <summary>
    /// Link-local addresses with scope IDs (<c>%eth0</c>, <c>%4</c>) are
    /// accepted — the character set inside the bracket is narrow enough
    /// that the percent sign itself is not a smuggler. Pinned here so a
    /// future tightening of the charset doesn't silently break real
    /// sysadmin setups using link-local proxies.
    /// </summary>
    [Theory]
    [InlineData("[fe80::1%eth0]:3128")]
    [InlineData("[fe80::1%4]:3128")]
    public void ValidateProxyEndpoint_accepts_ipv6_with_scope_id(string endpoint)
    {
        LaunchInputValidator.ValidateProxyEndpoint(endpoint);
    }

    /// <summary>
    /// IPv4-mapped IPv6 notation is permitted. Downstream code must not
    /// treat "bracketed implies pure IPv6" as a security boundary; this
    /// test pins that contract so a future assumption fails loudly here
    /// instead of subtly in the launcher.
    /// </summary>
    [Theory]
    [InlineData("[::ffff:127.0.0.1]:8080")]
    [InlineData("[::ffff:192.168.1.1]:443")]
    public void ValidateProxyEndpoint_accepts_ipv4_mapped_ipv6(string endpoint)
    {
        LaunchInputValidator.ValidateProxyEndpoint(endpoint);
    }

    [Theory]
    [InlineData(128)]  // exactly max
    [InlineData(64)]
    [InlineData(1)]
    public void ValidateProxyUsername_accepts_lengths_up_to_max(int length)
    {
        var name = new string('u', length);
        LaunchInputValidator.ValidateProxyUsername(name);
    }

    [Theory]
    [InlineData(129)]
    [InlineData(256)]
    public void ValidateProxyUsername_rejects_lengths_above_max(int length)
    {
        var name = new string('u', length);
        Assert.Throws<LaunchValidationException>(() => LaunchInputValidator.ValidateProxyUsername(name));
    }

    [Theory]
    [InlineData("host:0")]       // port zero
    [InlineData("host:65536")]   // port above range
    [InlineData("host")]         // missing port
    [InlineData(":8080")]        // missing host
    [InlineData("")]             // empty
    public void ValidateProxyEndpoint_rejects_malformed(string endpoint)
    {
        Assert.Throws<LaunchValidationException>(
            () => LaunchInputValidator.ValidateProxyEndpoint(endpoint));
    }
}
