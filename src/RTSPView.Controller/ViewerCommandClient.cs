using System.IO.Pipes;
using System.Text.Json;
using RTSPView.Core;

namespace RTSPView.Controller;

public sealed class ViewerCommandClient
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _snapshotGate = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Lazy<Task<ViewerCommandResult>>> _snapshots = new();
    private readonly string _pipeName;
    public ViewerCommandClient(string pipeName = "RTSPView.Commands.v1") => _pipeName = pipeName;

    public Task<ViewerCommandResult> SendAsync(ViewerCommandType type, int? slot, CancellationToken cancellationToken) =>
        SendAsync(new ViewerCommand(Guid.NewGuid(), type, slot), cancellationToken);

    public async Task<ViewerCommandResult> SendAsync(ViewerCommand command, CancellationToken cancellationToken)
    {
        if (command.Type != ViewerCommandType.CaptureCameraSnapshot)
            return await DispatchAsync(command, false, cancellationToken);
        if (command.Slot is not (>= 1 and <= StreamCatalog.MaximumSlot))
            return new(command.Id, false, "Invalid snapshot stream.");
        var slot = command.Slot.Value;
        var capture = _snapshots.GetOrAdd(slot, _ => new(() => CaptureAsync(command, slot), LazyThreadSafetyMode.ExecutionAndPublication));
        var result = await capture.Value.WaitAsync(cancellationToken);
        return result with { Id = command.Id };
    }

    private async Task<ViewerCommandResult> CaptureAsync(ViewerCommand command, int slot)
    {
        try { return await DispatchAsync(command, true, CancellationToken.None); }
        finally { _snapshots.TryRemove(slot, out _); }
    }

    private async Task<ViewerCommandResult> DispatchAsync(ViewerCommand command, bool snapshot, CancellationToken cancellationToken)
    {
        var gate = snapshot ? _snapshotGate : _gate;
        var entered = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(command.Type is ViewerCommandType.AutomationOverlays or ViewerCommandType.SensorAutomation or ViewerCommandType.Ping ? 1 : 8));
        try
        {
            await gate.WaitAsync(timeout.Token);
            entered = true;
            await using var pipe = new NamedPipeClientStream(".", snapshot ? _pipeName + ".Snapshots" : _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(command).AsMemory(), timeout.Token);
            var line = await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("Viewer disconnected before acknowledging the command.");
            var result = JsonSerializer.Deserialize<ViewerCommandResult>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Viewer returned an invalid response.");
            return result.Id == command.Id ? result : new ViewerCommandResult(command.Id, false, "Viewer returned a mismatched response.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ViewerCommandResult(Guid.Empty, false, "Viewer command timed out.");
        }
        catch (Exception) { return new ViewerCommandResult(Guid.Empty, false, "Viewer unavailable."); }
        finally { if (entered) gate.Release(); }
    }

}
