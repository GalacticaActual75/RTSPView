namespace RTSPView.Core;

public sealed record AircraftPhoto(string Url, string Link, string Photographer)
{
    public bool Representative { get; init; }
    public string Source { get; init; } = "Planespotters.net";
    public string License { get; init; } = "";
    public string Credit => "Photo © " + Photographer + " · " + Source + (License.Length > 0 ? " · " + License : "");
    public bool IsValid => ((Source == "Planespotters.net" && IsUrl(Url, "t.plnspttrs.net") && IsUrl(Link, "www.planespotters.net")) ||
        (Source == "Wikimedia Commons" && Representative && (IsUrl(Url,"upload.wikimedia.org") || IsUrl(Url,"thumb.wikimedia.org")) && IsUrl(Link,"commons.wikimedia.org") && License is { Length: > 0 and <= 80 })) &&
        Photographer is { Length: > 0 and <= 160 } && !Photographer.Any(char.IsControl);
    private static bool IsUrl(string value, string host) => value is { Length: <= 2048 } &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == host && uri.IsDefaultPort && uri.UserInfo.Length == 0;
}
