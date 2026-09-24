using System.IO;
namespace RTSPView.Viewer;

public partial class CameraTile
{
    private CameraTile? _sharedSource;
    private readonly List<CameraTile> _frameMirrors = [];
    private async Task<bool> RefreshSharedSnapshotAsync()
    {
        if (_sharedSource is null || _disposed || !await _sharedSource.RefreshSnapshotAsync()) return false;
        var temporary = Path.Combine(_snapshotDirectory, $"camera-{Slot}-{Guid.NewGuid():N}.jpg");
        try
        {
            File.Copy(Path.Combine(_snapshotDirectory, $"camera-{_sharedSource.Slot}.jpg"), temporary);
            File.Move(temporary, Path.Combine(_snapshotDirectory, $"camera-{Slot}.jpg"), true);
            Interlocked.Exchange(ref _snapshotCapturedTicks, DateTimeOffset.UtcNow.Ticks);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
        finally { try { File.Delete(temporary); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } }
    }
}
