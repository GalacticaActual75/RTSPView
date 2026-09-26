using System.Windows.Media.Imaging;
using RTSPView.Core;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("RTSPView.AircraftChecks")]
namespace RTSPView.Viewer;

// Accessed on the UI dispatcher. Share pending/successful images; retry failures after a cooldown.
internal sealed class AircraftImageCache(Func<AircraftPhoto, Task<BitmapSource?>> load, Func<DateTimeOffset>? clock = null)
{
    internal readonly Dictionary<string, Task<BitmapSource?>> Tasks = [];
    private readonly Dictionary<string, DateTimeOffset> _retryAfter = [];
    internal Task<BitmapSource?> Get(AircraftPhoto photo)
    {
        if (!photo.IsValid) return Task.FromResult<BitmapSource?>(null);
        var now = clock?.Invoke() ?? DateTimeOffset.UtcNow;
        if (Tasks.TryGetValue(photo.Url, out var task))
        {
            if (!task.IsCompleted || task.IsCompletedSuccessfully && task.Result is not null) return task;
            if (!_retryAfter.TryGetValue(photo.Url, out var retry)) { _retryAfter[photo.Url] = now.AddMinutes(1); return task; }
            if (now < retry) return task;
            Tasks.Remove(photo.Url); _retryAfter.Remove(photo.Url);
        }
        if (Tasks.Count >= 64) { var oldest = Tasks.Keys.First(); Tasks.Remove(oldest); _retryAfter.Remove(oldest); }
        return Tasks[photo.Url] = load(photo);
    }
}
