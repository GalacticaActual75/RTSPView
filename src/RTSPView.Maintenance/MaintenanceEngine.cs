using RTSPView.Core;

namespace RTSPView.Maintenance;

// The privileged boundary accepts fixed verbs and opaque operation IDs only.
public sealed class MaintenanceEngine
{
    private readonly Func<bool> _installed;
    private readonly Func<Task> _prepare;
    private readonly Func<Task<int>> _install;
    private readonly Action<MaintenanceStatus> _persist;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MaintenanceStatus _status = new() { Available = true, State = "idle", Message = "Maintenance helper ready." };
    public MaintenanceEngine(Func<bool> installed, Func<Task> prepare, Func<Task<int>> install, Action<MaintenanceStatus> persist, MaintenanceStatus? previous = null)
    {
        (_installed, _prepare, _install, _persist) = (installed, prepare, install, persist);
        if (previous is not null)
            _status = previous.State is "complete" or "failed" ? previous with { Available = true, OperationId = null }
                : new() { Available = true, State = "failed", Message = "Previous maintenance was interrupted. Check the host before retrying." };
    }
    public MaintenanceStatus Status => Volatile.Read(ref _status) with { PawnInstalled = _installed() };
    public bool Busy => _status.State is "downloading" or "installing";
    // Hold the operation gate through service shutdown so a new install cannot race stopping.
    public bool TryBeginStop() => _gate.Wait(0);

    private void Report(string state, string message, Guid? id = null, bool restart = false)
    {
        var status = new MaintenanceStatus { Available = true, State = state, Message = message, OperationId = id,
            PawnInstalled = _installed(), RestartRequired = restart };
        _persist(status); // No installation side effects unless its state was recorded first.
        Volatile.Write(ref _status, status);
    }
    public async Task<MaintenanceStatus> HandleAsync(MaintenanceRequest request)
    {
        if (request.Command == "status" && request.OperationId is null) return Status;
        if (request.Command is not ("prepare" or "install")) throw new ArgumentException("Unsupported maintenance command.");
        if (!await _gate.WaitAsync(0)) return Status;
        try
        {
            if (_installed()) { Report("complete", "PawnIO is already installed. No installation is needed."); return Status; }
            if (request.Command == "prepare")
            {
                if (request.OperationId is not null) throw new ArgumentException("Unexpected operation ID.");
                var id = Guid.NewGuid();
                Report("downloading", "Downloading and verifying PawnIO " + MaintenanceProtocol.PawnVersion + ".", id);
                await _prepare();
                Report("ready", "Verified PawnIO installer ready.", id);
            }
            else
            {
                if (_status.State != "ready" || request.OperationId is null || request.OperationId != _status.OperationId)
                    throw new ArgumentException("Prepare and verify the installer before installation.");
                Report("installing", "Installing PawnIO on this host. Do not power off the host.", request.OperationId);
                var code = await _install();
                if (code == 3010) Report("complete", "PawnIO installed. Restart Windows to finish driver setup.", restart: true);
                else if (code == 0 && _installed()) Report("complete", "PawnIO installed. Temperature sensors are being refreshed.");
                else Report("failed", $"PawnIO installation could not be confirmed (exit code {code}). Check the host before retrying.");
            }
            return Status;
        }
        catch (ArgumentException) { throw; }
        catch (Exception error)
        {
            Report("failed", $"PawnIO installation did not complete ({error.GetType().Name}). Check the host before retrying.");
            return Status;
        }
        finally { _gate.Release(); }
    }
}
