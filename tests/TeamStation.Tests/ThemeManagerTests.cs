using TeamStation.App.Services;

namespace TeamStation.Tests;

public sealed class ThemeManagerTests
{
    [Fact]
    public void PublishedThemesAllNormalizeToThemselves()
    {
        foreach (var theme in ThemeManager.Themes)
            Assert.Equal(theme.Id, ThemeManager.Normalize(theme.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NotATheme")]
    public void UnknownThemeFallsBackToDark(string? themeId)
    {
        Assert.Equal("Dark", ThemeManager.Normalize(themeId));
    }

    [Theory]
    [InlineData("system", "System")]
    [InlineData("dark", "Dark")]
    [InlineData("graphite", "Graphite")]
    [InlineData("light", "Light")]
    [InlineData("highcontrast", "HighContrast")]
    public void ThemeIdsNormalizeToCanonicalCasing(string themeId, string expected)
    {
        Assert.Equal(expected, ThemeManager.Normalize(themeId));
    }
}
