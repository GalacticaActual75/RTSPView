using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;
using Image = System.Windows.Controls.Image;

namespace RTSPView.Viewer;

// Copies VLC's decoded frames into a WPF visual so the window compositor can
// blend video and controls as one layer. Used by composited tiles and picture-in-picture overlays.
internal sealed class CompositedVideoPresenter : IDisposable
{
    private readonly Image _image;
    private readonly Action _sizeChanged;
    private readonly Dictionary<Image, Action> _mirrors = new();
    public void AddMirror(Image image, Action resized) { _mirrors[image] = resized; if (_bitmap is not null) { image.Source = _bitmap; resized(); } }
    public void RemoveMirror(Image image) => _mirrors.Remove(image);
    private readonly SemaphoreSlim _pixels = new(1, 1);
    private readonly DispatcherTimer _timer;
    private IntPtr _allocation;
    private IntPtr _buffer;
    private int _width, _height, _pitch, _bytes;
    private long _frame, _presented;
    public long UploadCount { get; private set; }
    private bool _active = true;
    private WriteableBitmap? _bitmap;

    public CompositedVideoPresenter(MediaPlayer player, Image image, Action sizeChanged)
    {
        _image = image;
        _sizeChanged = sizeChanged;
        player.SetVideoCallbacks(Lock, Unlock, Display);
        player.SetVideoFormatCallbacks(Setup, Cleanup);
        _timer = new DispatcherTimer(DispatcherPriority.Render, image.Dispatcher) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Present();
        _timer.Start();
    }

    private uint Setup(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        // Retain source resolution; reject unreasonable allocations rather than
        // allowing a damaged stream to allocate unbounded unmanaged memory.
        if (width == 0 || height == 0 || width > 8192 || height > 8192 || (ulong)width * height > 16_777_216) return 0;
        _pixels.Wait();
        try
        {
            FreeBuffer();
            _width = (int)width; _height = (int)height;
            _pitch = (_width * 4 + 31) & ~31;
            var alignedLines = (_height + 31) & ~31;
            _bytes = checked(_pitch * alignedLines);
            _allocation = Marshal.AllocHGlobal(_bytes + 31);
            _buffer = new IntPtr((_allocation.ToInt64() + 31) & ~31L);
            Marshal.Copy(new byte[] { (byte)'R', (byte)'V', (byte)'3', (byte)'2' }, 0, chroma, 4);
            pitches = (uint)_pitch; lines = (uint)alignedLines;
            _presented = Interlocked.Read(ref _frame);
            return 1;
        }
        catch (OutOfMemoryException) { FreeBuffer(); return 0; }
        finally { _pixels.Release(); }
    }

    private IntPtr Lock(IntPtr opaque, IntPtr planes)
    {
        _pixels.Wait();
        Marshal.WriteIntPtr(planes, _buffer);
        return IntPtr.Zero;
    }
    private void Unlock(IntPtr opaque, IntPtr picture, IntPtr planes) => _pixels.Release();
    private void Display(IntPtr opaque, IntPtr picture) => Interlocked.Increment(ref _frame);

    private void Present()
    {
        // Keep the decoder warm for automation, but do not upload invisible frames.
        // Leave _presented unchanged so revealing the image presents the newest frame.
        if (!_active || (!_image.IsVisible && !_mirrors.Keys.Any(image => image.IsVisible)) || _presented == Interlocked.Read(ref _frame) || !_pixels.Wait(0)) return;
        var resized = false;
        try
        {
            if (_buffer == IntPtr.Zero) return;
            if (_bitmap is null || _bitmap.PixelWidth != _width || _bitmap.PixelHeight != _height)
            {
                // RV32 contains an unused fourth byte, not an alpha channel.
                // Bgr32 treats every pixel as opaque before window alpha is applied.
                _bitmap = new WriteableBitmap(_width, _height, 96, 96, PixelFormats.Bgr32, null);
                _image.Source = _bitmap;
                resized = true;
            }
            _bitmap.WritePixels(new Int32Rect(0, 0, _width, _height), _buffer, _bytes, _pitch);
            UploadCount++;
            _presented = Interlocked.Read(ref _frame);
        }
        finally { _pixels.Release(); }
        if (resized) { _sizeChanged(); foreach (var mirror in _mirrors.ToArray()) { mirror.Key.Source = _bitmap; mirror.Value(); } }
    }

    public async Task<BitmapSource?> CaptureFrameAsync()
    {
        return await _image.Dispatcher.InvokeAsync(() =>
        {
            if (!_active || !_pixels.Wait(0)) return null;
            try
            {
                if (_buffer == IntPtr.Zero || Interlocked.Read(ref _frame) == 0) return null;
                var frame = BitmapSource.Create(_width, _height, 96, 96, PixelFormats.Bgr32, null, _buffer, _bytes, _pitch);
                BitmapSource preview = new TransformedBitmap(frame, new ScaleTransform(320d / _width, 320d / _width));
                preview.Freeze();
                return preview;
            }
            finally { _pixels.Release(); }
        });
    }

    // Call on the UI thread before replacing a player. Retired callbacks must
    // remain alive until their player is stopped/disposed on the worker thread.
    public void Deactivate()
    {
        _active = false;
        _timer.Stop();
    }
    private void Cleanup(ref IntPtr opaque)
    {
        _pixels.Wait();
        try { FreeBuffer(); }
        finally { _pixels.Release(); }
    }
    private void FreeBuffer()
    {
        if (_allocation != IntPtr.Zero) Marshal.FreeHGlobal(_allocation);
        _allocation = _buffer = IntPtr.Zero;
    }
    // The owner must stop/dispose VLC before releasing its callback buffers.
    public void Dispose()
    {
        _pixels.Wait();
        try { FreeBuffer(); }
        finally { _pixels.Release(); }
    }
}
