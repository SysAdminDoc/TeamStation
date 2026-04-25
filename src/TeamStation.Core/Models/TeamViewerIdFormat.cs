using System.Text;

namespace TeamStation.Core.Models;

/// <summary>
/// Canonical TeamViewer ID parsing rules used before imported or launched IDs
/// reach persistence or Process.Start. TeamViewer IDs are ASCII decimal digits
/// only; Unicode decimal digits are intentionally rejected.
/// </summary>
public static class TeamViewerIdFormat
{
    public const int MinLength = 8;
    public const int MaxLength = 12;

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (value.Length is < MinLength or > MaxLength)
            return false;

        foreach (var ch in value)
            if (!IsAsciiDigit(ch))
                return false;

        return true;
    }

    public static string ExtractAsciiDigits(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
            if (IsAsciiDigit(ch))
                sb.Append(ch);

        return sb.ToString();
    }

    private static bool IsAsciiDigit(char ch) => ch is >= '0' and <= '9';
}
