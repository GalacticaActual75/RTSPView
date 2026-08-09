using System.IO.Pipes;
using System.Text.Json;
using SpotMonitor.Core;

namespace SpotMonitor.Controller;

public sealed class ViewerCommandClient
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<ViewerCommandResult> SendAsync(ViewerCommandType type, int? slot, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var pipe = new NamedPipeClientStream(".", ViewerCommandServerPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            await pipe.ConnectAsync(timeout.Token);
            var command = new ViewerCommand(Guid.NewGuid(), type, slot);
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
        catch (Exception exception) { return new ViewerCommandResult(Guid.Empty, false, $"Viewer unavailable: {exception.Message}"); }
        finally { _gate.Release(); }
    }

    private const string ViewerCommandServerPipeName = "SpotMonitor.Commands.v1";
}
