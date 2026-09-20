using System.IO.Pipes;
using System.Text.Json;
using RTSPView.Core;

namespace RTSPView.Controller;

public sealed class ViewerCommandClient
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _pipeName;
    public ViewerCommandClient(string pipeName = "RTSPView.Commands.v1") => _pipeName = pipeName;

    public Task<ViewerCommandResult> SendAsync(ViewerCommandType type, int? slot, CancellationToken cancellationToken) =>
        SendAsync(new ViewerCommand(Guid.NewGuid(), type, slot), cancellationToken);

    public async Task<ViewerCommandResult> SendAsync(ViewerCommand command, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(command.Type is ViewerCommandType.AutomationOverlays or ViewerCommandType.SensorAutomation ? 1 : 8));
            await pipe.ConnectAsync(timeout.Token);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(command));
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
        finally { _gate.Release(); }
    }

}
