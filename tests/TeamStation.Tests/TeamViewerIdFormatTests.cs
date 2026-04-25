using TeamStation.Core.Models;

namespace TeamStation.Tests;

public class TeamViewerIdFormatTests
{
    [Theory]
    [InlineData("12345678")]
    [InlineData("123456789012")]
    public void IsValid_accepts_ascii_digit_ids_in_supported_length_range(string id)
    {
        Assert.True(TeamViewerIdFormat.IsValid(id));
    }

    [Theory]
    [InlineData("1234567")]
    [InlineData("1234567890123")]
    [InlineData("123 456 789")]
    [InlineData("\u0661\u0662\u0663\u0664\u0665\u0666\u0667\u0668\u0669")]
    [InlineData("\uFF11\uFF12\uFF13\uFF14\uFF15\uFF16\uFF17\uFF18\uFF19")]
    public void IsValid_rejects_non_ascii_or_out_of_range_ids(string id)
    {
        Assert.False(TeamViewerIdFormat.IsValid(id));
    }

    [Fact]
    public void ExtractAsciiDigits_ignores_unicode_decimal_digits()
    {
        var mixed = "TV-\u0661123 456 789-\uFF19";

        Assert.Equal("123456789", TeamViewerIdFormat.ExtractAsciiDigits(mixed));
    }
}
