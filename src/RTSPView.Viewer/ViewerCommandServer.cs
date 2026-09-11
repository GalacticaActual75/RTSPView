using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using RTSPView.Core;

namespace RTSPView.Viewer;

public sealed class ViewerCommandServer : IDisposable
{
    public const string PipeName = "RTSPView.Commands.v1";
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _worker;

    public ViewerCommandServer(Func<ViewerCommand, Task<ViewerCommandResult>> handler) => _worker = Task.Run(() => RunAsync(handler));

    private async Task RunAsync(Func<ViewerCommand, Task<ViewerCommandResult>> commandHandler)
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_cancellation.Token);
                using var reader = new StreamReader(pipe);
                await using var writer = new StreamWriter(pipe) { AutoFlush = true };
                while (pipe.IsConnected && !_cancellation.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(_cancellation.Token);
                    if (line is null) break;
                    ViewerCommandResult result;
                    try
                    {
                        var command = JsonSerializer.Deserialize<ViewerCommand>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                            ?? throw new InvalidDataException("Empty command.");
                        result = await commandHandler(command);
                    }
                    catch (Exception) { result = new ViewerCommandResult(Guid.Empty, false, "Viewer command failed."); }
                    await writer.WriteLineAsync(JsonSerializer.Serialize(result));
                }
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { await Task.Delay(500, _cancellation.Token).ConfigureAwait(false); }
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        try { _worker.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        _cancellation.Dispose();
    }
}
