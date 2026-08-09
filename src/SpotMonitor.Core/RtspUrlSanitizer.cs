namespace SpotMonitor.Core;

public static class RtspUrlSanitizer
{
    public static string Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.UserInfo)) return value;
        var builder = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty };
        var safe = builder.Uri.AbsoluteUri;
        var schemeEnd = safe.IndexOf("://", StringComparison.Ordinal);
        return schemeEnd < 0 ? safe : safe.Insert(schemeEnd + 3, "***:***@");
    }

    public static string RemoveCredentials(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.UserInfo)) return value;
        return new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty }.Uri.AbsoluteUri;
    }
}
