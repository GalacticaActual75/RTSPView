using RTSPView.Core;

internal static class StreamActivityChecks
{
    public static void Run()
    {
        var now = DateTimeOffset.UtcNow;
        var status = new CameraRuntimeStatus {State=CameraConnectionState.Live,LastFrameAt=now,LastError="Old watchdog error",ReconnectCount=2};
        Check(status.WithFrameProgress(now).LastError is not null,"no frames must not clear error");
        Check((status with {State=CameraConnectionState.Reconnecting}).WithFrameProgress(now.AddSeconds(1)).LastError is not null,"recovery still pending");
        Check((status with {NextReconnectAt=now.AddSeconds(5)}).WithFrameProgress(now.AddSeconds(1)).LastError is not null,"pending restart retains error");
        var recovered=status.WithFrameProgress(now.AddSeconds(1));
        Check(recovered.LastError is null && recovered.ReconnectCount==2,"real frame recovery clears error, preserves history");
        var stream = new StreamActivity();
        stream.BeginAttempt(now);
        stream.Observe(0, now.AddSeconds(1));
        Check(stream.LastFrameAt is null, "zero decoder count is not a received frame");
        Check(stream.Warning(now.AddSeconds(5), now)?.StartsWith("No video frames") == true, "connected without video is stale");
        stream.Observe(30, now.AddSeconds(2));
        Check(stream.FramesPerSecond == 30 && stream.Warning(now.AddSeconds(3), now) is null, "frame progress clears warning and measures FPS");
        stream.Observe(30, now.AddSeconds(7));
        Check(stream.FramesPerSecond == 0 && stream.Warning(now.AddSeconds(25), now) == "Last frame received 23 seconds ago", "stalled frames age continuously");
        stream.BeginAttempt(now.AddSeconds(26));
        stream.Observe(0, now.AddSeconds(27));
        Check(stream.LastFrameAt == now.AddSeconds(2), "reconnection does not make the old image fresh");
        stream.Observe(1, now.AddSeconds(28));
        Check(stream.Warning(now.AddSeconds(28), now.AddSeconds(26)) is null, "real new frame clears warning after reconnect");
        Console.WriteLine("Stream activity checks passed: initial silence, FPS, stall age and reconnect recovery.");
    }
    private static void Check(bool result, string message) { if (!result) throw new Exception(message); }
}
