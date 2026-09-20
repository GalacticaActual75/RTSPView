namespace RTSPView.Infrastructure;

// Shared by the viewer and controller. The file lock serializes intentional exit,
// manual launch and watchdog recovery across processes, including controller restarts.
public sealed class ViewerRuntimeState(string directory)
{
    public const string InstanceMutexName = "Local\\RTSPView.Viewer.SingleInstance";
    private string PausePath => Path.Combine(directory, "viewer-paused");
    public bool Paused => File.Exists(PausePath);
    public void Pause() => File.WriteAllText(PausePath, DateTimeOffset.UtcNow.ToString("O"));
    public void Resume() => File.Delete(PausePath);

    public async Task<bool> PrepareLaunchAsync(bool respectPause, CancellationToken token)
    {
        using var gate = await AcquireAsync(token);
        if (respectPause && Paused) return false;
        Resume();
        return true;
    }

    public async Task<FileStream> AcquireAsync(CancellationToken token)
    {
        Directory.CreateDirectory(directory);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(directory, "viewer-lifecycle.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(50, token); }
        }
    }

    public static bool IsRunning()
    {
        try
        {
            using var mutex = Mutex.OpenExisting(InstanceMutexName);
            // Observe the live instance handle without briefly taking ownership
            // away from a viewer that is starting on another thread/process.
            return true;
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
    }
}
