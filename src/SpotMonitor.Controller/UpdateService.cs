using System.Diagnostics;
using System.Reflection;
using SpotMonitor.Core;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using SpotMonitor.Infrastructure;

namespace SpotMonitor.Controller;

public sealed class UpdateService
{
    private static string ChannelPath(string channel) => UpdateRelease.ValidateChannel(channel) == "beta"
        ? @"UPDATE_CHANNEL_DIRECTORY" : @"UPDATE_CHANNEL_DIRECTORY";
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
        var channelPath = ChannelPath(channel);
        try
        {
            var manifestPath = Path.Combine(channelPath, "update.json");
            if (!File.Exists(manifestPath))
                return new(installed.Label, null, false, false, "No update has been published to this channel yet.", channelPath, installed.Channel, channel);

            await using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, cancellationToken: cancellationToken)
                ?? throw new InvalidDataException("The update manifest is empty.");
            Validate(manifest, channel);
            var installerPath = ResolveInstaller(manifest.Installer, channel);
            if (!File.Exists(installerPath)) throw new FileNotFoundException("The published installer is missing.", installerPath);
            var latest = UpdateRelease.Parse(manifest.Version);
            var updateAvailable = UpdateRelease.CanInstall(installed, latest, channel);
            return new(installed.Label, latest.Label, true, updateAvailable,
                updateAvailable ? $"SpotMonitor {manifest.Version} is ready to install." : "SpotMonitor is up to date on this channel.", channelPath, installed.Channel, channel);
        }
        catch (Exception exception)
        {
            return new(installed.Label, null, false, false, $"Update channel unavailable: {exception.Message}", channelPath, installed.Channel, channel);
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
            var manifestPath = Path.Combine(ChannelPath(channel), "update.json");
            await using var manifestStream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(manifestStream, cancellationToken: cancellationToken)
                ?? throw new InvalidDataException("The update manifest is empty.");
            Validate(manifest, channel);
            if (manifest.Version != expectedVersion) throw new InvalidOperationException("The available release changed. Check for updates and confirm the new version.");
            var latest = UpdateRelease.Parse(manifest.Version);
            if (!UpdateRelease.CanInstall(installed, latest, channel))
            {
                WriteProgress(progressPath, "complete", "SpotMonitor is already up to date. No changes were made.");
                return new(false, "SpotMonitor is already up to date.");
            }

            var source = ResolveInstaller(manifest.Installer, channel);
            var staged = Path.Combine(updateDirectory, Path.GetFileName(source));
            WriteProgress(progressPath, "working", $"Copying SpotMonitor {manifest.Version} from the LAN channel...");
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true))
            await using (var output = new FileStream(staged, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
                await input.CopyToAsync(output, cancellationToken);
            WriteProgress(progressPath, "working", "Verifying the downloaded installer...");
            var actualHash = await ComputeSha256Async(staged, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actualHash), Convert.FromHexString(manifest.Sha256)))
            {
                File.Delete(staged);
                throw new InvalidDataException("The installer checksum does not match the update manifest.");
            }

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
            _logger.Write("AUDIT", $"SpotMonitor {manifest.Version} update staged and elevation requested from web admin");
            return new(true, $"SpotMonitor {manifest.Version} is staged. Follow the update progress window on the Windows host. The page will disconnect during installation.");
        }
        catch (Exception exception)
        {
            if (progressPath is not null)
                try { WriteProgress(progressPath, "failed", $"Update did not start: {exception.Message}"); } catch { /* Preserve the original error. */ }
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
    private static void Validate(UpdateManifest manifest, string channel)
    {
        if (UpdateRelease.Parse(manifest.Version).Channel != channel) throw new InvalidDataException("The manifest does not match the selected channel.");
        if (string.IsNullOrWhiteSpace(manifest.Installer) || Path.GetFileName(manifest.Installer) != manifest.Installer || !manifest.Installer.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The installer filename is invalid.");
        if (!Regex.IsMatch(manifest.Sha256 ?? string.Empty, "^[A-Fa-f0-9]{64}$")) throw new InvalidDataException("The installer checksum is invalid.");
    }

    private static string ResolveInstaller(string filename, string channel)
    {
        var root = Path.GetFullPath(ChannelPath(channel)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(root, filename));
        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The installer path escapes the update channel.");
        return resolved;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}

public sealed record UpdateManifest(string Version, string Installer, string Sha256, DateTimeOffset PublishedAt);
public sealed record UpdateStatus(string InstalledVersion, string? LatestVersion, bool ChannelAvailable, bool UpdateAvailable, string Message, string ChannelPath, string InstalledChannel, string SelectedChannel);
public sealed record UpdateLaunchResult(bool Started, string Message);
