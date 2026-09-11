using System.IO.Pipes;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using SpotMonitor.Core;

namespace SpotMonitor.Viewer;

public sealed class ViewerTelemetryPublisher : IDisposable
{
    public const string PipeName = "SpotMonitor.Telemetry.v1";
    private readonly Channel<ViewerTelemetry> _updates = Channel.CreateBounded<ViewerTelemetry>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _worker;

    public ViewerTelemetryPublisher() => _worker = Task.Run(RunAsync);
    public void Publish(ViewerTelemetry telemetry) => _updates.Writer.TryWrite(telemetry);

    private async Task RunAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_cancellation.Token);
                await using var writer = new StreamWriter(pipe, Encoding.UTF8, 1024, true) { AutoFlush = true };
                while (pipe.IsConnected && await _updates.Reader.WaitToReadAsync(_cancellation.Token))
                    if (_updates.Reader.TryRead(out var snapshot)) await writer.WriteLineAsync(JsonSerializer.Serialize(snapshot));
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { await Task.Delay(500, _cancellation.Token).ConfigureAwait(false); }
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _updates.Writer.TryComplete();
        try { _worker.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        _cancellation.Dispose();
    }
}
