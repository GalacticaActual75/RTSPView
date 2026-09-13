using System.Diagnostics;
using System.Text.Json;
using RTSPView.Core;
using RTSPView.Infrastructure;

namespace RTSPView.Maintenance;

public sealed class ServiceUpdateCoordinator
{
    private readonly string _root, _state, _allowedSid;
    private string? _jobDirectory;
    private Guid? _operation;
    public ServiceUpdateCoordinator(string root, string state, string allowedSid)
    {
        (_root, _state, _allowedSid) = (root, state, allowedSid);
        var pointer = Path.Combine(state, "app-update.json");
        if (File.Exists(pointer) && Guid.TryParse(File.ReadAllText(pointer), out var id))
        { _operation = id; _jobDirectory = Path.Combine(state, "Updates", id.ToString("N")); }
    }

    public MaintenanceStatus? Status
    {
        get
        {
            if (_jobDirectory is null) return null;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(_jobDirectory, "progress.json")));
                var state = document.RootElement.GetProperty("state").GetString();
                if (state == "working" && !WorkerOrInstallerRunning())
                    return new() { Available = true, SupportsAppUpdates = true, State = "update-failed", Message = "The update worker stopped before reporting completion. Check the installed version before retrying." };
                return new() { Available = true, SupportsAppUpdates = true, OperationId = _operation,
                    State = state == "working" ? "update-installing" : state == "ready" ? "update-ready" : "update-" + state,
                    Message = document.RootElement.GetProperty("message").GetString() ?? "RTSPView update in progress.",
                    ProgressPath = Path.Combine(_jobDirectory, "progress.json") };
            }
            catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException)
            { return new() { Available = true, SupportsAppUpdates = true, State = "update-installing", Message = "Checking RTSPView update progress." }; }
        }
    }
    public bool Busy => Status?.State == "update-installing";

    private bool WorkerOrInstallerRunning()
    {
        try
        {
            var workerPath = Path.Combine(_jobDirectory!, "worker.json");
            if (!File.Exists(workerPath)) return true; // The handoff is still recording its worker.
            var worker = JsonSerializer.Deserialize<WorkerIdentity>(File.ReadAllText(workerPath))!;
            try
            {
                using var process = Process.GetProcessById(worker.Id);
                if (!process.HasExited && process.StartTime.ToUniversalTime() == worker.StartedAt) return true;
            }
            catch (ArgumentException) { }
            // A worker crash must not allow a second installer to race its surviving child.
            foreach (var process in Process.GetProcessesByName("installer"))
            {
                using (process)
                    if (string.Equals(process.MainModule?.FileName, Path.Combine(_jobDirectory!, "installer.exe"), StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        catch { return true; } // If Windows cannot prove completion, keep other maintenance blocked.
    }
    private sealed record WorkerIdentity(int Id, DateTime StartedAt);

    public static void ValidateSelection(string? channel, string? version)
    {
        if (channel is null || version is null || UpdateRelease.Parse(version).Channel != UpdateRelease.ValidateChannel(channel))
            throw new InvalidDataException("Confirm a valid RTSPView release and matching channel.");
    }

    public async Task<MaintenanceStatus> PrepareAsync(MaintenanceRequest request, uint session)
    {
        ValidateSelection(request.Channel, request.Version);
        if (request.OperationId is not null || session == 0) throw new InvalidDataException("Prepare the update from the signed-in viewer session.");
        var id = Guid.NewGuid();
        var directory = Path.Combine(_state, "Updates", id.ToString("N"));
        Directory.CreateDirectory(directory); HelperSetup.ValidateProtectedPath(directory);
        // Ignore repository overrides inherited from the user environment at the privileged boundary.
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        using var source = new GitHubUpdateSource(http, GitHubUpdateSource.DefaultRepository, Path.Combine(_state, "github-cooldown.json"));
        var release = await source.FindAsync(request.Channel!) ?? throw new IOException("The selected update channel has no release.");
        if (!release.Manifest.SupportsServiceUpdates) throw new InvalidDataException("This older installer requires the standard Windows approval update path.");
        if (release.Manifest.Version != request.Version) throw new InvalidDataException("The release changed. Check for updates and confirm the current version.");
        var installedPath = Path.Combine(_root, "Controller", "SpotMonitor.Controller.exe");
        var installed = UpdateRelease.Parse(FileVersionInfo.GetVersionInfo(installedPath).ProductVersion!.Split('+')[0]);
        if (!UpdateRelease.CanInstall(installed, UpdateRelease.Parse(request.Version!), request.Channel!)) throw new InvalidDataException("This release is already installed or older than the installed release.");
        await source.DownloadAsync(release, Path.Combine(directory, "installer.exe"));
        var script = Path.Combine(AppContext.BaseDirectory, "Apply-ServiceUpdate.ps1");
        HelperSetup.ValidateProtectedPath(script);
        File.Copy(script, Path.Combine(directory, "Apply-ServiceUpdate.ps1"));
        File.WriteAllText(Path.Combine(directory, "job.json"), JsonSerializer.Serialize(new
        { InstallRoot = _root, Version = request.Version, Sha256 = release.Manifest.Sha256, AllowedSid = _allowedSid, SessionId = session }));
        File.WriteAllText(Path.Combine(directory, "progress.json"), JsonSerializer.Serialize(new { state = "ready", message = "Verified RTSPView update ready.", windowSession = "service" }));
        File.WriteAllText(Path.Combine(_state, "app-update.json"), id.ToString());
        _jobDirectory = directory; _operation = id;
        return Status!;
    }

    public MaintenanceStatus Launch(MaintenanceRequest request)
    {
        if (request.OperationId is null || request.OperationId != _operation || Status?.State != "update-ready" || request.Channel is not null || request.Version is not null)
            throw new InvalidDataException("Prepare and verify the selected RTSPView update first.");
        File.WriteAllText(Path.Combine(_jobDirectory!, "progress.json"), JsonSerializer.Serialize(new { state = "working", message = "Installing verified RTSPView update without another UAC prompt.", windowSession = "service" }));
        try
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _jobDirectory! };
            foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(_jobDirectory!, "Apply-ServiceUpdate.ps1") }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("The service update worker could not start.");
            // A recording failure cannot mark a running worker as failed and permit a second update.
            try { File.WriteAllText(Path.Combine(_jobDirectory!, "worker.json"), JsonSerializer.Serialize(new WorkerIdentity(process.Id, process.StartTime.ToUniversalTime()))); }
            catch { /* Missing identity keeps maintenance conservatively blocked. */ }
            return Status!;
        }
        catch
        {
            File.WriteAllText(Path.Combine(_jobDirectory!, "progress.json"), JsonSerializer.Serialize(new { state = "failed", message = "The service update worker could not start.", windowSession = "service" }));
            throw;
        }
    }
}
