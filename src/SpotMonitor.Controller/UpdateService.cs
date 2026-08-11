using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using SpotMonitor.Infrastructure;

namespace SpotMonitor.Controller;

public sealed class UpdateService
{
    public const string ChannelPath = @"UPDATE_CHANNEL_DIRECTORY";
    private readonly string _dataDirectory;
    private readonly RollingFileLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public UpdateService(string dataDirectory, RollingFileLogger logger)
    {
        _dataDirectory = dataDirectory;
        _logger = logger;
    }

    public async Task<UpdateStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        var installed = NormalizeVersion(typeof(UpdateService).Assembly.GetName().Version);
        try
        {
            var manifestPath = Path.Combine(ChannelPath, "update.json");
            if (!File.Exists(manifestPath))
                return new(installed, null, false, false, "No update has been published to the LAN channel yet.", ChannelPath);

            await using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, cancellationToken: cancellationToken)
                ?? throw new InvalidDataException("The update manifest is empty.");
            Validate(manifest);
            var installerPath = ResolveInstaller(manifest.Installer);
            if (!File.Exists(installerPath)) throw new FileNotFoundException("The published installer is missing.", installerPath);
            var latest = Version.Parse(manifest.Version);
            var updateAvailable = latest > installed;
            return new(installed, latest, true, updateAvailable,
                updateAvailable ? $"SpotMonitor {latest.ToString(3)} is ready to install." : "SpotMonitor is up to date.", ChannelPath);
        }
        catch (Exception exception)
        {
            return new(installed, null, false, false, $"Update channel unavailable: {exception.Message}", ChannelPath);
        }
    }

    public async Task<UpdateLaunchResult> StageAndLaunchAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var manifestPath = Path.Combine(ChannelPath, "update.json");
            await using var manifestStream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(manifestStream, cancellationToken: cancellationToken)
                ?? throw new InvalidDataException("The update manifest is empty.");
            Validate(manifest);
            var latest = Version.Parse(manifest.Version);
            var installed = NormalizeVersion(typeof(UpdateService).Assembly.GetName().Version);
            if (latest <= installed) return new(false, "SpotMonitor is already up to date.");

            var source = ResolveInstaller(manifest.Installer);
            var updateDirectory = Path.Combine(_dataDirectory, "updates");
            Directory.CreateDirectory(updateDirectory);
            var staged = Path.Combine(updateDirectory, Path.GetFileName(source));
            File.Copy(source, staged, true);
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
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", updater, "-InstallerPath", staged, "-ExpectedSha256", manifest.Sha256 })
                startInfo.ArgumentList.Add(argument);
            _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Windows did not start the update helper.");
            _logger.Write("AUDIT", $"SpotMonitor {latest.ToString(3)} update staged and elevation requested from web admin");
            return new(true, $"SpotMonitor {latest.ToString(3)} is staged. Approve the Windows prompt on the camera-wall host; the page will disconnect while the update installs.");
        }
        finally { _gate.Release(); }
    }

    private static Version NormalizeVersion(Version? version) => new(version?.Major ?? 0, version?.Minor ?? 0, Math.Max(0, version?.Build ?? 0));

    private static void Validate(UpdateManifest manifest)
    {
        if (!Version.TryParse(manifest.Version, out var version) || version.Build < 0) throw new InvalidDataException("The update version is invalid.");
        if (string.IsNullOrWhiteSpace(manifest.Installer) || Path.GetFileName(manifest.Installer) != manifest.Installer || !manifest.Installer.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The installer filename is invalid.");
        if (!Regex.IsMatch(manifest.Sha256 ?? string.Empty, "^[A-Fa-f0-9]{64}$")) throw new InvalidDataException("The installer checksum is invalid.");
    }

    private static string ResolveInstaller(string filename)
    {
        var root = Path.GetFullPath(ChannelPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
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
public sealed record UpdateStatus(Version InstalledVersion, Version? LatestVersion, bool ChannelAvailable, bool UpdateAvailable, string Message, string ChannelPath);
public sealed record UpdateLaunchResult(bool Started, string Message);
