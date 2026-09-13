namespace RTSPView.Core;

public sealed record UpdateStatus(string InstalledVersion, string? LatestVersion, bool ChannelAvailable, bool UpdateAvailable,
    string Message, string ChannelPath, string InstalledChannel, string SelectedChannel,
    DateTimeOffset? LastChecked = null, DateTimeOffset? NextCheck = null, bool ShowWallNotifications = true,
    DateTimeOffset? RetryAt = null, bool CheckFailed = false);

public sealed record WallUpdateNotice(bool Visible, string? Version, string Channel);
public sealed record WallUpdateRequest(string Version, string Channel, bool Confirmed);
public sealed record WallUpdateResponse(bool Started, string Message);
