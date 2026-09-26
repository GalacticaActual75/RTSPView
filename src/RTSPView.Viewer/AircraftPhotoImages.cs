using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using RTSPView.Core;

namespace RTSPView.Viewer;

internal static class AircraftPhotoImages
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 2_000_000 };
    internal static readonly AircraftImageCache Cache = new(Load);
    private static readonly SemaphoreSlim Slots = new(2);
    public static Task<BitmapSource?> Get(AircraftPhoto photo) => Cache.Get(photo);
    private static async Task<BitmapSource?> Load(AircraftPhoto photo)
    {
        await Slots.WaitAsync();
        try
        {
            var bytes = await Download(Http, photo, CancellationToken.None);
            return await Task.Run<BitmapSource?>(() =>
            {
                using var stream = new MemoryStream(bytes);
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 420; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
            });
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or NotSupportedException or IOException or ArgumentException or FormatException or InvalidOperationException or System.Runtime.InteropServices.COMException) { System.Diagnostics.Trace.TraceWarning("Aircraft photo download failed: {0}", e.Message); return null; }
        finally { Slots.Release(); }
    }
    internal static async Task<byte[]> Download(HttpClient http, AircraftPhoto photo, CancellationToken token)
    {
        var url = photo.Url;
        for (var redirects = 0; redirects <= 3; redirects++)
        {
            if (!(photo with { Url = url }).IsValid) throw new HttpRequestException("Untrusted aircraft image address.");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("RTSPView/1.0.47 (+https://github.com/GalacticaActual75/RTSPView/issues)");
            request.Headers.Accept.ParseAdd("image/jpeg, image/png, image/gif, image/bmp");
            using var response = await http.SendAsync(request, token);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                if (response.Headers.Location is not { } location) throw new HttpRequestException("Missing image redirect address.");
                url = new Uri(new Uri(url), location).AbsoluteUri; continue;
            }
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(token);
            if (bytes.Length > 2_000_000) throw new HttpRequestException("Aircraft image exceeds size limit.");
            return bytes;
        }
        throw new HttpRequestException("Too many aircraft image redirects.");
    }
}
