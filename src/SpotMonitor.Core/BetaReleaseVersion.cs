using System.Text.RegularExpressions;

namespace SpotMonitor.Core;

public static class BetaReleaseVersion
{
    public static Version Parse(string value)
    {
        var match = Regex.Match(value, @"^(\d{1,6})\.(\d{1,6})\.(\d{1,6})-beta\.(\d{1,6})$");
        if (!match.Success) throw new InvalidDataException("The beta channel requires a version such as 1.0.29-beta.3.");
        return new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value),
            int.Parse(match.Groups[3].Value), int.Parse(match.Groups[4].Value));
    }
}
