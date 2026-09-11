namespace RTSPView.Core;

public static class AppPaths
{
    public static string DataDirectory
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("RTSPVIEW_DATA_DIR") ?? Environment.GetEnvironmentVariable("SPOTMONITOR_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return DefaultDataDirectory(local);
        }
    }

    public static string DefaultDataDirectory(string localApplicationData)
    {
        var legacy = Path.Combine(localApplicationData, "SpotMonitor");
        // Keep installed cameras, hashes and channel choices in place during the rebrand.
        return File.Exists(Path.Combine(legacy, "settings.json")) || File.Exists(Path.Combine(legacy, "web-security.json"))
            ? legacy : Path.Combine(localApplicationData, "RTSPView");
    }
}
