using System.Text;
using TeamStation.Core.Models;
using TeamStation.Launcher;

namespace TeamStation.Tests;

/// <summary>
/// v0.3.4: <see cref="TeamViewerLauncher"/> gains a byte[]-aware Launch
/// overload that zeros the password buffers immediately after argv has
/// been composed (or, on failure, immediately after the launch attempt
/// fails). This is the most security-relevant property of the new path:
/// the cleartext password lives in our address space for the absolute
/// minimum window the launch flow allows.
/// </summary>
public class TeamViewerLauncherZeroingTests
{
    [Fact]
    public void Launch_with_byte_overload_zeros_password_after_failed_launch()
    {
        // Force a failure path: TeamViewer.exe path resolves to a nonexistent
        // file, which makes LaunchViaCli throw FileNotFoundException. The
        // try/finally in the byte[] Launch overload must still zero the
        // input byte arrays before returning.
        var launcher = new TeamViewerLauncher(() => null);
        var entry = new ConnectionEntry { Name = "x", TeamViewerId = "123456789" };
        var pw = Encoding.UTF8.GetBytes("super-secret");
        var proxyPw = Encoding.UTF8.GetBytes("proxy-secret");

        var outcome = launcher.Launch(entry, pw, proxyPw);

        Assert.False(outcome.Success);
        Assert.NotNull(outcome.Error);
        // The whole point: the input buffers MUST be zeroed even on the
        // error path (caller relies on this contract — bytes were handed
        // over and there's no way for the caller to know which exception
        // path the launcher took).
        Assert.All(pw, b => Assert.Equal(0, b));
        Assert.All(proxyPw, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Launch_with_byte_overload_zeros_proxy_password_when_only_main_is_provided()
    {
        var launcher = new TeamViewerLauncher(() => null);
        var entry = new ConnectionEntry { Name = "x", TeamViewerId = "123456789" };
        var pw = Encoding.UTF8.GetBytes("only-main");

        var outcome = launcher.Launch(entry, pw, proxyPasswordBytes: null);

        Assert.False(outcome.Success);
        Assert.All(pw, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Launch_with_byte_overload_handles_null_password_buffer_gracefully()
    {
        var launcher = new TeamViewerLauncher(() => null);
        var entry = new ConnectionEntry { Name = "x", TeamViewerId = "123456789" };

        // Both null: parity with the original string path when entry.Password
        // and entry.Proxy.Password are both unset. The launcher must not
        // throw a NullReferenceException trying to zero a null array.
        var outcome = launcher.Launch(entry, passwordBytes: null, proxyPasswordBytes: null);
        Assert.False(outcome.Success); // still fails because path is null, but cleanly
    }

    [Fact]
    public void Launch_with_byte_overload_zeros_even_if_validation_throws()
    {
        // Validation error path: a leading-dash password trips
        // LaunchInputValidator.ValidatePassword. The byte[] must still be
        // zeroed because the try/finally is at the Launch overload boundary,
        // outside the validation try/catch.
        var launcher = new TeamViewerLauncher(() => @"C:\Windows\System32\cmd.exe");
        var entry = new ConnectionEntry { Name = "x", TeamViewerId = "123456789" };
        var bad = Encoding.UTF8.GetBytes("-leading-dash");

        var outcome = launcher.Launch(entry, bad, proxyPasswordBytes: null);
        Assert.False(outcome.Success);
        Assert.All(bad, b => Assert.Equal(0, b));
    }
}
