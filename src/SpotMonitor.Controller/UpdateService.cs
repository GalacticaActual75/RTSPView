using System.Diagnostics;
using System.Reflection;
using SpotMonitor.Core;
using System.Text.Json;
using SpotMonitor.Infrastructure;

namespace SpotMonitor.Controller;

public sealed class UpdateService : IDisposable
{
    private readonly GitHubUpdateSource _source = new();
    private readonly string _dataDirectory;
    private readonly RollingFileLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _launched;
    private string? _activeProgressPath;
    private readonly UpdateChannelStore _channels;

    public UpdateService(string dataDirectory, RollingFileLogger logger)
    {
        _dataDirectory = dataDirectory;
        _logger = logger;
        _channels = new UpdateChannelStore(dataDirectory);
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
                updateAvailable ? $"RTSPView {manifest.Version} is ready to install." : "SpotMonitor is up to date on this channel.", channelPath, installed.Channel, channel);
        }
        catch (Exception)
        {
            return new(installed.Label, null, false, false, "GitHub updates unavailable. Check the Internet connection and public repository access.", channelPath, installed.Channel, channel);
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

    public async Task<UpdateLaunchResult> StageAndLaunchAsync(string expectedChannel, string expectedVersion, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        string? progressPath = null;
        try
        {
            var installed = InstalledRelease();
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
            var release = await _source.FindAsync(channel, refresh: true, cancellationToken: cancellationToken)
                ?? throw new InvalidDataException("No GitHub release is available for this channel.");
            var manifest = release.Manifest;
            if (manifest.Version != expectedVersion) throw new InvalidOperationException("The available release changed. Check for updates and confirm the new version.");
            var latest = UpdateRelease.Parse(manifest.Version);
            if (!UpdateRelease.CanInstall(installed, latest, channel))
            {
                WriteProgress(progressPath, "complete", "SpotMonitor is already up to date. No changes were made.");
                return new(false, "SpotMonitor is already up to date.");
            }

            var staged = Path.Combine(updateDirectory, manifest.Installer);
            WriteProgress(progressPath, "working", $"Downloading and verifying RTSPView {manifest.Version} from GitHub...");
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
            WriteProgress(progressPath, "working", "Waiting for Windows approval. Approve the administrator prompt on this host if shown.");
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", updater, "-InstallerPath", staged, "-ExpectedSha256", manifest.Sha256, "-StatusPath", progressPath, "-ExpectedVersion", manifest.Version })
                startInfo.ArgumentList.Add(argument);
            _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Windows did not start the update helper.");
            _launched = true;
            _activeProgressPath = progressPath;
            _logger.Write("AUDIT", $"RTSPView {manifest.Version} update staged and elevation requested from web admin");
            return new(true, $"RTSPView {manifest.Version} is staged. Follow the update progress window on the Windows host. The page will disconnect during installation.");
        }
        catch (Exception)
        {
            if (progressPath is not null)
                try { WriteProgress(progressPath, "failed", "Update did not start. Check server logs."); } catch { /* Preserve the original error. */ }
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

    private static UpdateRelease InstalledRelease()
    {
        var assembly = typeof(UpdateService).Assembly;
        var label = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];
        return UpdateRelease.Parse(label ?? "0.0.0");
    }
    public void Dispose() { _source.Dispose(); _gate.Dispose(); }
}

public sealed record UpdateStatus(string InstalledVersion, string? LatestVersion, bool ChannelAvailable, bool UpdateAvailable, string Message, string ChannelPath, string InstalledChannel, string SelectedChannel);
public sealed record UpdateLaunchResult(bool Started, string Message);
