using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RTSPView.Core;

namespace RTSPView.Controller;

// Interactive local-user commands; no unauthenticated HTTP installation endpoint.
public sealed class WallUpdateServer(UpdateMonitor monitor, RTSPView.Infrastructure.RollingFileLogger logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream("RTSPView.WallUpdates.v1", PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stoppingToken);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var bytes = new List<byte>(); var buffer = new byte[1];
                while (bytes.Count < 4096 && await pipe.ReadAsync(buffer, timeout.Token) == 1 && buffer[0] != 10) bytes.Add(buffer[0]);
                if (bytes.Count >= 4096) throw new InvalidDataException();
                var request = JsonSerializer.Deserialize<WallUpdateRequest>(Encoding.UTF8.GetString(bytes.ToArray())) ?? throw new InvalidDataException();
                WallUpdateResponse result;
                try { result = await monitor.InstallFromWallAsync(request, stoppingToken); }
                catch (Exception error) { var reference = Guid.NewGuid().ToString("N")[..8]; logger.Write("ERROR", $"Reference {reference}: Wall update failed ({error.GetType().Name})."); result = new(false, $"Unable to start the update. Reference {reference}. Open Settings → Updates."); }
                await using var writer = new StreamWriter(pipe) {AutoFlush=true};
                await writer.WriteLineAsync(JsonSerializer.Serialize(result));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) when (error is IOException or JsonException or OperationCanceledException or UnauthorizedAccessException)
            { await Task.Delay(500, stoppingToken); }
        }
    }
}
