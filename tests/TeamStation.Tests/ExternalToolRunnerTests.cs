using TeamStation.Core.Models;
using TeamStation.Core.Services;

namespace TeamStation.Tests;

public class ExternalToolRunnerTests
{
    [Fact]
    public void Expand_replaces_connection_tag_and_environment_tokens()
    {
        const string envName = "TEAMSTATION_TEST_EXTERNAL_TOOL_ENV";
        var previous = Environment.GetEnvironmentVariable(envName);
        Environment.SetEnvironmentVariable(envName, "from-env");
        try
        {
            var expanded = ExternalToolRunner.Expand(
                "%ID%|%NAME%|%PASSWORD%|%PROFILE%|%TAG:site%|%TAG:missing%|${TEAMSTATION_TEST_EXTERNAL_TOOL_ENV}",
                CreateEntry());

            Assert.Equal("123456789|Front Desk|secret|Support|HQ||from-env", expanded);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, previous);
        }
    }

    [Fact]
    public void CreateToolStartInfo_expands_and_preserves_quoted_arguments()
    {
        var tool = new ExternalToolDefinition
        {
            Name = "Diagnostics",
            Command = "\"C:\\Program Files\\Diagnostics\\diag.exe\"",
            Arguments = "--id %ID% --name \"%NAME%\" --empty \"\" --site %TAG:site%",
        };

        var psi = ExternalToolRunner.CreateToolStartInfo(tool, CreateEntry());

        Assert.Equal("C:\\Program Files\\Diagnostics\\diag.exe", psi.FileName);
        Assert.False(psi.UseShellExecute);
        Assert.Equal(
            new[] { "--id", "123456789", "--name", "Front Desk", "--empty", "", "--site", "HQ" },
            psi.ArgumentList);
    }

    [Fact]
    public void CreateToolStartInfo_rejects_arguments_embedded_in_quoted_command()
    {
        var tool = new ExternalToolDefinition
        {
            Name = "Diagnostics",
            Command = "\"C:\\Program Files\\Diagnostics\\diag.exe\" --unexpected",
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => ExternalToolRunner.CreateToolStartInfo(tool, CreateEntry()));
        Assert.Contains("Arguments field", ex.Message);
    }

    [Fact]
    public void CreateToolStartInfo_rejects_unterminated_quoted_arguments()
    {
        var tool = new ExternalToolDefinition
        {
            Name = "Diagnostics",
            Command = "diag.exe",
            Arguments = "--name \"Front Desk",
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => ExternalToolRunner.CreateToolStartInfo(tool, CreateEntry()));
        Assert.Contains("unterminated quoted value", ex.Message);
    }

    [Fact]
    public void CreateScriptStartInfo_runs_hidden_noninteractive_powershell_with_expanded_command()
    {
        var psi = ExternalToolRunner.CreateScriptStartInfo("Write-Output \"%NAME%\"", CreateEntry());

        Assert.NotNull(psi);
        Assert.Equal("powershell.exe", psi.FileName);
        Assert.False(psi.UseShellExecute);
        Assert.True(psi.CreateNoWindow);
        Assert.Contains("-NoProfile", psi.ArgumentList);
        Assert.Contains("-NonInteractive", psi.ArgumentList);
        Assert.Contains("-ExecutionPolicy", psi.ArgumentList);
        Assert.Contains("Bypass", psi.ArgumentList);
        Assert.Equal("Write-Output \"Front Desk\"", psi.ArgumentList[psi.ArgumentList.Count - 1]);
    }

    [Fact]
    public void CreateScriptStartInfo_skips_blank_scripts_after_expansion()
    {
        const string envName = "TEAMSTATION_TEST_MISSING_SCRIPT_ENV";
        var previous = Environment.GetEnvironmentVariable(envName);
        Environment.SetEnvironmentVariable(envName, null);
        try
        {
            var psi = ExternalToolRunner.CreateScriptStartInfo("${TEAMSTATION_TEST_MISSING_SCRIPT_ENV}", CreateEntry());

            Assert.Null(psi);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, previous);
        }
    }

    [Fact]
    public void SplitArguments_preserves_empty_and_escaped_quote_arguments()
    {
        var args = ExternalToolRunner.SplitArguments("--empty \"\" --quoted \"a\\\"b\" tail");

        Assert.Equal(new[] { "--empty", "", "--quoted", "a\"b", "tail" }, args);
    }

    private static ConnectionEntry CreateEntry() => new()
    {
        Name = "Front Desk",
        TeamViewerId = "123456789",
        Password = "secret",
        ProfileName = "Support",
        Tags = ["site=HQ", "role=kiosk"],
    };
}
