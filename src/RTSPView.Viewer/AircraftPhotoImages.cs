using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using RTSPView.Core;

namespace RTSPView.Viewer;

internal static class AircraftPhotoImages
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 2_000_000 };
    internal static readonly AircraftImageCache Cache = new(Load);
    internal static Action<string>? Log { get; set; }
    private static readonly SemaphoreSlim Slots = new(2);
    public static Task<BitmapSource?> Get(AircraftPhoto photo) => Cache.Get(photo);
    private static async Task<BitmapSource?> Load(AircraftPhoto photo)
    {
        await Slots.WaitAsync();
        try
        {
            var bytes = await Download(Http, photo, CancellationToken.None);
            var decoded = await Task.Run<BitmapSource?>(() =>
            {
                using var stream = new MemoryStream(bytes);
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 420; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
            });
            Log?.Invoke($"image={photo.DiagnosticId} source={photo.Source} result=decoded width={decoded!.PixelWidth} height={decoded.PixelHeight}");
            return decoded;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or NotSupportedException or IOException or ArgumentException or FormatException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { Log?.Invoke($"image={photo.DiagnosticId} source={photo.Source} result=failed error={e.GetType().Name} status={(e is HttpRequestException h ? (int?)h.StatusCode : null)}"); return null; }
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
