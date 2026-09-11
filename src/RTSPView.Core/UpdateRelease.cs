using System.Text.RegularExpressions;

namespace RTSPView.Core;

public sealed record UpdateRelease(string Label, Version Number, string Channel)
{
    public static string ValidateChannel(string channel) => channel is "stable" or "beta"
        ? channel : throw new InvalidDataException("Choose stable or beta as the update channel.");

    public static UpdateRelease Parse(string label)
    {
        var match = Regex.Match(label ?? "", @"^(\d{1,6})\.(\d{1,6})\.(\d{1,6})(?:-beta\.(\d{1,6}))?$");
        if (!match.Success) throw new InvalidDataException("The release version is invalid.");
        var beta = match.Groups[4].Success;
        return new(label!, new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value),
            int.Parse(match.Groups[3].Value), beta ? int.Parse(match.Groups[4].Value) : 0), beta ? "beta" : "stable");
    }

    public static bool CanInstall(UpdateRelease installed, UpdateRelease available, string selectedChannel)
    {
        ValidateChannel(selectedChannel);
        if (available.Channel != selectedChannel) throw new InvalidDataException("The published release does not match the selected channel.");
        // Switching channels is explicit and may intentionally install a lower version.
        return installed.Channel != selectedChannel || available.Number > installed.Number;
    }
}
