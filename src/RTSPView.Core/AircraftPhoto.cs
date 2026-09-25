namespace RTSPView.Core;

public sealed record AircraftPhoto(string Url, string Link, string Photographer)
{
    public bool IsValid => IsUrl(Url, "t.plnspttrs.net") && IsUrl(Link, "www.planespotters.net") &&
        Photographer is { Length: > 0 and <= 160 } && !Photographer.Any(char.IsControl);
    private static bool IsUrl(string value, string host) => value is { Length: <= 2048 } &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == host && uri.IsDefaultPort && uri.UserInfo.Length == 0;
}
