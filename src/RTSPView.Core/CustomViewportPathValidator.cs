using System.Globalization;
using System.Text.RegularExpressions;

namespace RTSPView.Core;

public static class CustomViewportPathValidator
{
    public const int MaximumPathLength = 65_536;
    private const int MaximumCommandCount = 4_096;
    private const int MaximumNumberCount = 16_384;
    private const double MaximumCoordinateMagnitude = 1_000_000;
    private static readonly Regex NumberPattern = new(
        @"[-+]?(?:(?:\d+\.?\d*)|(?:\.\d+))(?:[eE][-+]?\d+)?",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public static bool IsValid(
        string? pathData,
        double viewBoxX,
        double viewBoxY,
        double viewBoxWidth,
        double viewBoxHeight)
    {
        if (string.IsNullOrWhiteSpace(pathData) || pathData.Length > MaximumPathLength ||
            !IsFiniteBound(viewBoxX) || !IsFiniteBound(viewBoxY) ||
            !IsFiniteDimension(viewBoxWidth) || !IsFiniteDimension(viewBoxHeight))
            return false;

        var hasMove = false;
        var commandCount = 0;
        foreach (var character in pathData)
        {
            if (char.IsWhiteSpace(character) || char.IsDigit(character) ||
                character is '+' or '-' or '.' or ',')
                continue;
            if (character is 'e' or 'E') continue;
            if ("MmZzLlHhVvCcSsQqTtAa".Contains(character, StringComparison.Ordinal))
            {
                commandCount++;
                hasMove |= character is 'M' or 'm';
                if (commandCount > MaximumCommandCount) return false;
                continue;
            }
            return false;
        }

        if (!hasMove) return false;
        MatchCollection numbers;
        try { numbers = NumberPattern.Matches(pathData); }
        catch (RegexMatchTimeoutException) { return false; }
        if (numbers.Count is 0 or > MaximumNumberCount) return false;
        foreach (Match match in numbers)
        {
            if (!double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                !double.IsFinite(number) || Math.Abs(number) > MaximumCoordinateMagnitude)
                return false;
        }
        return true;
    }

    public static string NormalizeSourceName(string? sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName)) return "Custom SVG";
        var leaf = sourceName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
            ?? "Custom SVG";
        var sanitized = new string(leaf.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (sanitized.Length == 0) return "Custom SVG";
        return sanitized.Length <= 128 ? sanitized : sanitized[..128];
    }

    private static bool IsFiniteBound(double value) =>
        double.IsFinite(value) && Math.Abs(value) <= MaximumCoordinateMagnitude;

    private static bool IsFiniteDimension(double value) =>
        double.IsFinite(value) && value > 0 && value <= MaximumCoordinateMagnitude;
}
