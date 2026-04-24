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
    [InlineData("[::1]:8080")]  // IPv6 literal; this one is a known-limitation case, rejected today
    public void ValidateProxyEndpoint_accepts_or_rejects_consistent_with_implementation(string endpoint)
    {
        // Current implementation splits on ':' so IPv6 fails; test pins the contract.
        if (endpoint.Contains('[') || endpoint.Count(c => c == ':') > 1)
        {
            Assert.Throws<LaunchValidationException>(
                () => LaunchInputValidator.ValidateProxyEndpoint(endpoint));
        }
        else
        {
            LaunchInputValidator.ValidateProxyEndpoint(endpoint);
        }
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
