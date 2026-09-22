using System.IO.Pipes;
using System.Text.Json;
using System.Diagnostics;
using RTSPView.Controller;
using RTSPView.Core;
internal static class CommandIsolationChecks
{
    public static async Task Run()
    {
        var name="RTSPView-test-"+Guid.NewGuid().ToString("N");
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var captureConnected=new SemaphoreSlim(0);
        using var releaseCapture=new SemaphoreSlim(0);
        async Task Serve(string pipeName,bool stalled) {
            await using var server=new NamedPipeServerStream(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync(timeout.Token);
            using var reader=new StreamReader(server,leaveOpen:true);
            await using var writer=new StreamWriter(server,leaveOpen:true){AutoFlush=true};
            var command=JsonSerializer.Deserialize<ViewerCommand>((await reader.ReadLineAsync(timeout.Token))!)!;
            if(stalled){captureConnected.Release();await releaseCapture.WaitAsync(timeout.Token);}
            await writer.WriteLineAsync(JsonSerializer.Serialize(new ViewerCommandResult(command.Id,true,"Done")));
        }
        var snapshots=Serve(name+".Snapshots",true);var commands=Serve(name,false);
        var client=new ViewerCommandClient(name);
        var first=client.SendAsync(ViewerCommandType.CaptureCameraSnapshot,1,timeout.Token);
        await captureConnected.WaitAsync(timeout.Token);
        var duplicate=client.SendAsync(ViewerCommandType.CaptureCameraSnapshot,1,timeout.Token);
        var watch=Stopwatch.StartNew();
        var automation=await client.SendAsync(ViewerCommandType.SensorAutomation,null,timeout.Token);
        if(!automation.Success || watch.Elapsed>TimeSpan.FromSeconds(1) || first.IsCompleted)throw new Exception("Snapshot delayed automation");
        releaseCapture.Release();
        var results=await Task.WhenAll(first,duplicate);
        if(results.Any(r=>!r.Success)||results[0].Id==results[1].Id)throw new Exception("Snapshot deduplication lost a caller result");
        await Task.WhenAll(snapshots,commands);
        Console.WriteLine("PASS stalled snapshot isolation and duplicate coalescing");
    }
}
