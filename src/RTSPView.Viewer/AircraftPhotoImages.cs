using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using RTSPView.Core;

namespace RTSPView.Viewer;

internal static class AircraftPhotoImages
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = 2_000_000 };
    private static readonly Dictionary<string, Task<BitmapSource?>> Cache = [];
    private static readonly SemaphoreSlim Slots = new(2);
    // Called on the dispatcher; shared tasks avoid fetching again on every render.
    public static Task<BitmapSource?> Get(AircraftPhoto photo)
    {
        if (!photo.IsValid) return Task.FromResult<BitmapSource?>(null);
        if (Cache.TryGetValue(photo.Url, out var task)) return task;
        if (Cache.Count >= 64) Cache.Remove(Cache.Keys.First());
        return Cache[photo.Url] = Load(photo.Url);
    }
    private static async Task<BitmapSource?> Load(string url)
    {
        await Slots.WaitAsync();
        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            return await Task.Run<BitmapSource?>(() =>
            {
                using var stream = new MemoryStream(bytes);
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 420; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
            });
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or NotSupportedException or IOException or ArgumentException or FormatException or InvalidOperationException or System.Runtime.InteropServices.COMException) { return null; }
        finally { Slots.Release(); }
    }
}
