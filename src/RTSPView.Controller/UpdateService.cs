using System.Diagnostics;
using System.Reflection;
using RTSPView.Core;
using System.Text.Json;
using RTSPView.Infrastructure;
using RTSPView.Hardware;

namespace RTSPView.Controller;

public sealed class UpdateService : IDisposable
{
    private readonly GitHubUpdateSource _source;
    private readonly string _dataDirectory;
    private readonly RollingFileLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _launched;
    private string? _activeProgressPath;
    private readonly UpdateChannelStore _channels;
    private readonly MaintenanceClient? _maintenance;
    public Func<Task<bool>>? ExternalMaintenanceActive { get; set; }

    public UpdateService(string dataDirectory, RollingFileLogger logger, MaintenanceClient? maintenance = null)
    {
        _dataDirectory = dataDirectory;
        _logger = logger;
        _maintenance = maintenance;
        _channels = new UpdateChannelStore(dataDirectory);
        _source = new GitHubUpdateSource(Path.Combine(dataDirectory, "github-cooldown.json"));
    }

    public UpdateStatus InitialStatus()
    {
        var installed = InstalledRelease();
        var channel = _channels.Read(installed.Channel);
        return new(installed.Label, null, false, false, "Waiting for the daily update check.", _source.ChannelLocation(channel), installed.Channel, channel);
    }

    public async Task<UpdateStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        var installed = InstalledRelease();
        var channel = _channels.Read(installed.Channel);
        var channelPath = _source.ChannelLocation(channel);
        try
        {
            var release = await _source.FindAsync(channel, cancellationToken: cancellationToken);
            if (release is null) return new(installed.Label, null, false, false, "No GitHub release has been published to this channel yet.", channelPath, installed.Channel, channel);
            var manifest = release.Manifest;
            var latest = UpdateRelease.Parse(manifest.Version);
            var updateAvailable = UpdateRelease.CanInstall(installed, latest, channel);
            return new(installed.Label, latest.Label, true, updateAvailable,
                updateAvailable ? $"RTSPView {manifest.Version} is ready to install." : "RTSPView is up to date on this channel.", channelPath, installed.Channel, channel);
        }
        catch (GitHubRateLimitException error)
        {
            return new(installed.Label, null, false, false, "GitHub requested a pause. Update checks will resume automatically.", channelPath, installed.Channel, channel, RetryAt: error.RetryAt, CheckFailed: true);
        }
        catch (Exception)
        {
            return new(installed.Label, null, false, false, "GitHub updates unavailable. Check the Internet connection and public repository access.", channelPath, installed.Channel, channel, CheckFailed: true);
        }
    }

    public async Task SelectChannelAsync(string channel, CancellationToken cancellationToken = default)
    {
        UpdateRelease.ValidateChannel(channel);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_launched) throw new InvalidOperationException("Wait for the running update to finish before changing channels.");
            _channels.Save(channel);
            _logger.Write("AUDIT", $"Update channel selected: {channel}. No installation requested.");
        }
        finally { _gate.Release(); }
    }

    public async Task<string?> CheckPatchAsync(CancellationToken token)
    {
        var installed = InstalledRelease();
        if (_channels.Read(installed.Channel) != installed.Channel) return null;
        var patch = await _source.FindPatchAsync(installed, cancellationToken: token);
        return patch is not null && patch.Manifest.SupportsServiceUpdates &&
            UpdateRelease.CanInstall(installed, UpdateRelease.Parse(patch.Manifest.Version), installed.Channel)
            ? patch.Manifest.Version : null;
    }

    // Called only from the restart scheduler, inside TryRunMaintenanceAsync's gate.
    // It never opens a progress window or falls back to an elevation prompt.
    public async Task<UpdateLaunchResult> InstallScheduledPatchAsync(string version, Func<bool> stillEnabled, CancellationToken token)
    {
        var installed = InstalledRelease();
        if (!stillEnabled() || UpdateRelease.Parse(version).Patch == 0 || _channels.Read(installed.Channel) != installed.Channel ||
            !UpdateRelease.CanInstall(installed, UpdateRelease.Parse(version), installed.Channel))
            return new(false, "Automatic patch installation is disabled or the patch selection changed.");
        if (_maintenance is null) return new(false, "Automatic patches require an available maintenance helper.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(12));
        var helper = await _maintenance.SendAsync(new("status"), timeout.Token);
        if (_launched && helper.State is "update-failed" or "update-ready") _launched = false;
        if (_launched) return new(false, "Waiting for the previous application update to finish.", MaintenancePending: true);
        if (helper is not { Available: true, SupportsAppPatches: true })
            return new(false, "Enable an up-to-date maintenance helper to install patches automatically.");
        var ready = await _maintenance.SendAsync(new("prepare-update", Channel: installed.Channel, Version: version), timeout.Token);
        if (ready.State != "update-ready" || ready.OperationId is null || ready.ProgressPath is null) throw new IOException(ready.Message);
        // Recheck after the download: opting out while preparation runs cancels this handoff.
        if (!stillEnabled()) return new(false, "Automatic installation canceled; the patch remains available for manual installation.");
        _launched = true;
        _activeProgressPath = ready.ProgressPath;
        try
        {
            var launched = await _maintenance.SendAsync(new("install-update", ready.OperationId), timeout.Token);
            if (launched.State != "update-installing") throw new IOException(launched.Message);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or JsonException or UnauthorizedAccessException)
        {
            // A lost response cannot prove that the service did not launch its worker.
            _logger.Write("WARNING", "Scheduled patch handoff is unconfirmed; further restarts are deferred until helper status is known.");
            return new(false, "Patch installation was requested but its status is unconfirmed. Check the maintenance helper before retrying.", MaintenancePending: true);
        }
        _logger.Write("AUDIT", $"Scheduled application patch {version} accepted by the maintenance helper.");
        return new(true, "Application patch installation started at the scheduled restart.");
    }

    private Task<GitHubUpdate?> FindSelectedAsync(string channel, string version, CancellationToken token)
        => UpdateRelease.Parse(version).Patch > 0
            ? _source.FindPatchAsync(InstalledRelease(), refresh: true, cancellationToken: token)
            : _source.FindAsync(channel, refresh: true, cancellationToken: token);

    // Serialize scheduled maintenance with update staging, including the elevated installer handoff.
    public async Task<bool> TryRunMaintenanceAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken)) return false;
        try
        {
            var directory = Path.Combine(_dataDirectory, "updates");
            if (ExternalMaintenanceActive is not null && await ExternalMaintenanceActive()) return false;
            if (_launched && _activeProgressPath is not null)
            {
                try
                {
                    using var active = JsonDocument.Parse(File.ReadAllText(_activeProgressPath));
                    var state = active.RootElement.GetProperty("state").GetString();
                    if (state is not ("complete" or "failed" or "ready")) return false;
                    if (state is "failed" or "ready") _launched = false;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException) { return false; }
            }
            if (Directory.Exists(directory))
            {
                foreach (var path in Directory.EnumerateFiles(directory, "progress-*.json"))
                {
                    // Recent unfinished helpers also protect a newly restarted Controller.
                    if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddHours(-2) && path != _activeProgressPath) continue;
                    try
                    {
                        using var progress = JsonDocument.Parse(File.ReadAllText(path));
                        if (progress.RootElement.GetProperty("state").GetString() is not ("complete" or "failed")) return false;
                    }
                    catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException) { return false; }
                }
            }
            await action();
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<UpdateLaunchResult> StageAndLaunchAsync(string expectedChannel, string expectedVersion, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        string? progressPath = null;
        try
        {
            var installed = InstalledRelease();
            if (ExternalMaintenanceActive is not null && await ExternalMaintenanceActive())
                return new(false, "Wait for PawnIO maintenance to finish before installing an RTSPView update.");
            var channel = _channels.Read(installed.Channel);
            if (channel != UpdateRelease.ValidateChannel(expectedChannel)) throw new InvalidOperationException("The selected channel changed. Check for updates again.");
            if (_launched && _activeProgressPath is not null)
            {
                try
                {
                    using var status = JsonDocument.Parse(File.ReadAllText(_activeProgressPath));
                    if (status.RootElement.GetProperty("state").GetString() == "failed") _launched = false;
                }
                catch { /* A running helper may be replacing the status file. */ }
            }
            if (_launched) return new(false, "An update has already been launched. Check the progress window on the Windows host.");
            var updateDirectory = Path.Combine(_dataDirectory, "updates");
            Directory.CreateDirectory(updateDirectory);
            progressPath = Path.Combine(updateDirectory, $"progress-{Guid.NewGuid():N}.json");
            WriteProgress(progressPath, "working", $"Checking the {channel} update channel...");
            var progressScript = Path.Combine(AppContext.BaseDirectory, "Show-UpdateProgress.ps1");
            if (!File.Exists(progressScript)) throw new FileNotFoundException("The update progress helper is missing.", progressScript);
            var progressStart = new ProcessStartInfo("powershell.exe") { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var argument in new[] { "-NoProfile", "-STA", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", progressScript, "-StatusPath", progressPath })
                progressStart.ArgumentList.Add(argument);
            _ = Process.Start(progressStart) ?? throw new InvalidOperationException("Windows did not start the update progress window.");
            var approvalReason = "The maintenance helper is not configured for this Controller.";
            if (_maintenance is not null)
            {
                MaintenanceStatus? helper = null;
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    helper = await _maintenance.SendAsync(new("status"), timeout.Token);
                }
                catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException or JsonException)
                {
                    approvalReason = error is UnauthorizedAccessException
                        ? "The maintenance helper could not authenticate this Windows account."
                        : "The maintenance helper did not return a usable response. Check its status under Settings > System.";
                    _logger.Write("WARNING", $"Maintenance update connection failed ({error.GetType().Name}); Windows approval will be required.");
                }
                if (helper is not null)
                    approvalReason = !helper.Available ? "The maintenance helper is unavailable. Check its status under Settings > System."
                        : !helper.SupportsAppUpdates ? "The running maintenance helper does not support application updates."
                        : "This release requires the Windows approval installation path.";
                var serviceCompatible = false;
                if (helper is { Available: true, SupportsAppUpdates: true } && (UpdateRelease.Parse(expectedVersion).Patch == 0 || helper.SupportsAppPatches))
                {
                    var candidate = await FindSelectedAsync(channel, expectedVersion, cancellationToken);
                    if (candidate?.Manifest.Version != expectedVersion) throw new InvalidOperationException("The available release changed. Check for updates and confirm the new version.");
                    serviceCompatible = candidate.Manifest.SupportsServiceUpdates;
                }
                if (serviceCompatible)
                {
                    WriteProgress(progressPath, "working", "The enabled helper is downloading and verifying the confirmed release. No additional Windows approval is needed.");
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
                    var ready = await _maintenance.SendAsync(new("prepare-update", Channel: channel, Version: expectedVersion), timeout.Token);
                    if (ready.State != "update-ready" || ready.OperationId is null || ready.ProgressPath is null) throw new IOException(ready.Message);
                    var serviceProgress = Path.GetFullPath(ready.ProgressPath);
                    var expectedDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Maintenance", "State", "Updates", ready.OperationId.Value.ToString("N")));
                    if (!string.Equals(serviceProgress, Path.Combine(expectedDirectory, "progress.json"), StringComparison.OrdinalIgnoreCase)) throw new IOException("Unexpected service update progress location.");
                    var progress = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
                    foreach (var argument in new[] { "-NoProfile", "-STA", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", Path.Combine(AppContext.BaseDirectory, "Show-UpdateProgress.ps1"), "-StatusPath", serviceProgress, "-WindowSession", "service" }) progress.ArgumentList.Add(argument);
                    WriteProgress(progressPath, "complete", "Download verified. Installation progress is opening.");
                    using var progressWindow = Process.Start(progress) ?? throw new IOException("The update progress window could not start.");
                    var launched = await _maintenance.SendAsync(new("install-update", ready.OperationId), timeout.Token);
                    if (launched.State != "update-installing") throw new IOException(launched.Message);
                    _launched = true; _activeProgressPath = serviceProgress;
                    _logger.Write("AUDIT", $"RTSPView {expectedVersion} update accepted by the maintenance helper.");
                    return new(true, $"RTSPView {expectedVersion} is installing through the enabled helper. No additional UAC approval is required. The page will disconnect during installation.");
                }
            }

            _logger.Write("WARNING", "Update is using Windows approval: " + approvalReason);
            var release = await FindSelectedAsync(channel, expectedVersion, cancellationToken)
                ?? throw new InvalidDataException("No GitHub release is available for this channel.");
            var manifest = release.Manifest;
            if (manifest.Version != expectedVersion) throw new InvalidOperationException("The available release changed. Check for updates and confirm the new version.");
            var latest = UpdateRelease.Parse(manifest.Version);
            if (!UpdateRelease.CanInstall(installed, latest, channel))
            {
                WriteProgress(progressPath, "complete", "RTSPView is already up to date. No changes were made.");
                return new(false, "RTSPView is already up to date.");
            }

            var staged = Path.Combine(updateDirectory, manifest.Installer);
            WriteProgress(progressPath, "working", $"{approvalReason} Windows approval will be required. Downloading and verifying RTSPView {manifest.Version} from GitHub...");
            await _source.DownloadAsync(release, staged, cancellationToken);

            var updater = Path.Combine(AppContext.BaseDirectory, "Apply-Update.ps1");
            if (!File.Exists(updater)) throw new FileNotFoundException("The update helper is missing.", updater);
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            WriteProgress(progressPath, "working", approvalReason + " Approve the administrator prompt on this host to continue.");
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", updater, "-InstallerPath", staged, "-ExpectedSha256", manifest.Sha256, "-StatusPath", progressPath, "-ExpectedVersion", manifest.Version })
                startInfo.ArgumentList.Add(argument);
            _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Windows did not start the update helper.");
            _launched = true;
            _activeProgressPath = progressPath;
            _logger.Write("AUDIT", $"RTSPView {manifest.Version} update staged and elevation requested from web admin");
            return new(true, $"RTSPView {manifest.Version} is staged. Follow the update progress window on the Windows host. The page will disconnect during installation.");
        }
        catch (Exception error)
        {
            var reference = Guid.NewGuid().ToString("N")[..8];
            _logger.Write("ERROR", $"Reference {reference}: Update did not start ({error.GetType().Name}).");
            if (progressPath is not null)
                try { WriteProgress(progressPath, "failed", $"Update did not start. Reference {reference}. Open Settings → Updates."); } catch { /* Preserve the original error. */ }
            throw;
        }
        finally { _gate.Release(); }
    }

    private static void WriteProgress(string path, string state, string message)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new { state, message, windowSession = "staging", updatedAt = DateTimeOffset.UtcNow }));
        File.Move(temporary, path, true);
    }

    public static UpdateRelease InstalledRelease()
    {
        var assembly = typeof(UpdateService).Assembly;
        var label = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];
        return UpdateRelease.Parse(label ?? "0.0.0");
    }
    public void Dispose() { _source.Dispose(); _gate.Dispose(); }
}

public sealed record UpdateLaunchResult(bool Started, string Message, bool MaintenancePending = false);
