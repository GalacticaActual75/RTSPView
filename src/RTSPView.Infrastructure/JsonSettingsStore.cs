using System.Text.Json;
using System.Security.Cryptography;
using RTSPView.Core;

namespace RTSPView.Infrastructure;

public sealed class JsonSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private string BackupPath => _path + ".bak";

    public JsonSettingsStore(string path) => _path = path;

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => LoadCoreAsync(cancellationToken, false);

    private async Task<AppSettings> LoadCoreAsync(CancellationToken cancellationToken, bool writeLockHeld)
    {
        if (!File.Exists(_path) && !File.Exists(BackupPath)) return new AppSettings { StorageRevision = "" };
        try
        {
            return await ReadAndValidateAsync(_path, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException)
        {
            await using var lease = writeLockHeld ? null : await AcquireWriteAsync(cancellationToken);
            if (!writeLockHeld && File.Exists(_path))
            {
                try { return await ReadAndValidateAsync(_path, cancellationToken); }
                catch (Exception error) when (error is JsonException or IOException or InvalidDataException) { }
            }
            if (!File.Exists(BackupPath)) throw new InvalidDataException("The configuration and its backup are unavailable or invalid.", exception);
            var recovered = await ReadAndValidateAsync(BackupPath, cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            DurableJson.PreserveInvalid(_path);
            var recovery = _path + ".recovery-" + Guid.NewGuid().ToString("N");
            try { File.Copy(BackupPath, recovery); File.Move(recovery, _path, true); }
            finally { if (File.Exists(recovery)) File.Delete(recovery); }
            return recovered;
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await using var lease = await AcquireWriteAsync(cancellationToken);
        await SaveCoreAsync(settings, cancellationToken);
    }

    public async Task<WriteSession> BeginWriteAsync(CancellationToken token = default) => new(this, await AcquireWriteAsync(token));
    public sealed class WriteSession : IAsyncDisposable
    {
        private readonly JsonSettingsStore _store;
        private readonly FileStream _lease;
        private bool _disposed;
        internal WriteSession(JsonSettingsStore store, FileStream lease) => (_store, _lease) = (store, lease);
        public Task<AppSettings> LoadAsync(CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _store.LoadCoreAsync(token, true);
        }
        public Task SaveAsync(AppSettings settings, CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _store.SaveCoreAsync(settings, token);
        }
        public ValueTask DisposeAsync() { _disposed = true; return _lease.DisposeAsync(); }
    }

    private async Task<FileStream> AcquireWriteAsync(CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException e) when (IsSharingViolation(e) && attempt < 100) { await Task.Delay(50, token); }
        }
    }

    private static string Revision(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public async Task<AppSettings> SaveCameraEditsAsync(AppSettings original, AppSettings draft, bool replacing = false, CancellationToken token = default)
    {
        await using var lease = await AcquireWriteAsync(token);
        var latest = await LoadCoreAsync(token, true);
        if (replacing)
        {
            if (latest.StorageRevision != original.StorageRevision) throw new InvalidDataException("Configuration changed in another editor. Close and reopen Streams before importing again. Your import has not been applied.");
            draft.StorageRevision = latest.StorageRevision;
        }
        else
        {
            var cameras = latest.Cameras.ToArray();
            foreach (var edited in draft.Cameras.Where(c => c != original.Cameras.FirstOrDefault(o => o.Slot == c.Slot)))
            {
                var index = Array.FindIndex(cameras, c => c.Slot == edited.Slot);
                if (index < 0 || latest.DeletedCameraSlots.Contains(edited.Slot) || cameras[index] != original.Cameras.First(c => c.Slot == edited.Slot))
                    throw new InvalidDataException($"Stream {edited.Slot} changed in another editor. Close and reopen Streams to review it; your changes have not been applied.");
                cameras[index] = edited;
            }
            draft = latest with { Cameras = cameras };
        }
        await SaveCoreAsync(draft, token);
        return await LoadCoreAsync(token, true);
    }

    private async Task SaveCoreAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var normalized = Validate(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var currentRevision = File.Exists(_path) ? Revision(await File.ReadAllBytesAsync(_path, cancellationToken)) : "";
        if (settings.StorageRevision is { } expected && currentRevision != expected)
            throw new InvalidDataException("Configuration changed in another editor. Reload this page before saving; your changes have not been applied.");
        // Keep the original file beyond the rolling .bak so schema-14 releases can be restored.
        var migrationBackup = _path + ".before-layouts.json";
        if (File.Exists(_path) && !File.Exists(migrationBackup))
        {
            using var original = JsonDocument.Parse(await File.ReadAllTextAsync(_path, cancellationToken));
            if (original.RootElement.TryGetProperty("SchemaVersion", out var schema) && schema.GetInt32() < 15)
                File.Copy(_path, migrationBackup, false);
        }
        var weatherBackup = _path + ".before-weather.json";
        if (File.Exists(_path) && !File.Exists(weatherBackup))
        {
            using var original = JsonDocument.Parse(await File.ReadAllTextAsync(_path, cancellationToken));
            if (original.RootElement.TryGetProperty("SchemaVersion", out var oldSchema) && oldSchema.GetInt32() < 16)
                File.Copy(_path, weatherBackup, false);
        }
        var temporary = _path + ".tmp";
        await WriteAsync(temporary, normalized, cancellationToken);
        // Windows readers from the Viewer, Controller or an older process may
        // briefly deny replacement. Keep the complete temporary file and retry
        // the atomic swap; never delete the live settings as a workaround.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(_path)) File.Replace(temporary, _path, BackupPath, true);
                else File.Move(temporary, _path);
                settings.StorageRevision = Revision(await File.ReadAllBytesAsync(_path, CancellationToken.None));
                break;
            }
            catch (IOException error) when (IsSharingViolation(error) && attempt < 20)
            {
                await Task.Delay(50, cancellationToken);
            }
        }
    }

    public async Task ExportWithoutCredentialsAsync(AppSettings settings, string destination, CancellationToken cancellationToken = default)
    {
        await WriteAsync(destination, WithoutCredentials(settings), cancellationToken);
    }

    public static AppSettings WithoutCredentials(AppSettings settings)
    {
        var normalized = settings.Normalize();
        var sanitized = normalized with
        {
            Cameras = normalized.Cameras.Select(camera => camera with { RtspUrl = RtspUrlSanitizer.RemoveCredentials(camera.RtspUrl) }).ToArray(),
            Camera = normalized.Camera with { RtspUrl = RtspUrlSanitizer.RemoveCredentials(normalized.Camera.RtspUrl) },
            AdditionalOverlays = normalized.AdditionalOverlays.Select(overlay => overlay with
            {
                Camera = overlay.Camera with { RtspUrl = RtspUrlSanitizer.RemoveCredentials(overlay.Camera.RtspUrl) }
            }).ToArray(),
            DoorbellOverlay = normalized.DoorbellOverlay with
            {
                Camera = normalized.DoorbellOverlay.Camera with
                {
                    RtspUrl = RtspUrlSanitizer.RemoveCredentials(normalized.DoorbellOverlay.Camera.RtspUrl)
                }
            },
            GarageOverlay = normalized.GarageOverlay with
            {
                Camera = normalized.GarageOverlay.Camera with
                {
                    RtspUrl = RtspUrlSanitizer.RemoveCredentials(normalized.GarageOverlay.Camera.RtspUrl)
                }
            }
        };
        return Validate(sanitized);
    }

    public Task<AppSettings> ImportAsync(string source, CancellationToken cancellationToken = default) => ReadAndValidateAsync(source, cancellationToken);

    public static AppSettings ParseImport(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Select a RTSPView configuration JSON file.");
        var properties = document.RootElement.EnumerateObject().ToArray();
        if (!properties.Any(p => p.Name.Equals("SchemaVersion", StringComparison.OrdinalIgnoreCase)) ||
            !properties.Any(p => p.Name.Equals("Cameras", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("Camera", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The file must contain a schema version and camera configuration.");
        if (properties.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new InvalidDataException("Configuration contains duplicate fields.");
        var settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Configuration is empty.");
        if (settings.SchemaVersion < 1 || settings.Cameras is null || settings.Camera is null || settings.Cameras.Any(c => c is null))
            throw new InvalidDataException("Configuration contains invalid camera or schema fields.");
        return Validate(settings);
    }

    public async Task<string> ImportAndSaveAsync(string json, CancellationToken cancellationToken = default)
    {
        var settings = ParseImport(json);
        var previous = await LoadAsync(cancellationToken);
        var backup = _path + ".before-import-" + Guid.NewGuid().ToString("N") + ".json";
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await WriteAsync(backup, previous, cancellationToken);
        await SaveAsync(settings, cancellationToken);
        return Path.GetFileName(backup);
    }

    private static async Task<AppSettings> ReadAndValidateAsync(string path, CancellationToken cancellationToken)
    {
        // Permit an atomic settings replacement while this reader finishes its
        // consistent snapshot of the old file.
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        var settings = JsonSerializer.Deserialize<AppSettings>(bytes, JsonOptions)
            ?? throw new InvalidDataException("Configuration is empty.");
        try { return Validate(settings) with { StorageRevision = Revision(bytes) }; }
        catch (Exception error) when (error is NullReferenceException or ArgumentException)
        { throw new InvalidDataException("Configuration contains invalid nested values.", error); }
    }

    private static AppSettings Validate(AppSettings settings)
    {
        if (settings.AdditionalOverlays is null || settings.AdditionalOverlays.Count > AppSettings.MaximumAdditionalOverlays ||
            settings.AdditionalOverlays.Any(overlay => overlay is null))
            throw new InvalidDataException("Configuration supports up to 16 overlays and cannot contain empty overlay entries.");
        if (settings.SchemaVersion > AppSettings.CurrentSchemaVersion)
            throw new InvalidDataException($"Configuration schema {settings.SchemaVersion} is newer than this application supports.");
        var normalized = settings.Normalize();
        foreach (var camera in normalized.Cameras)
        {
            ValidateCameraUrl(camera, $"Camera {camera.Slot}");
        }
        ValidateCameraUrl(normalized.DoorbellOverlay.Camera, "Doorbell");
        ValidateCameraUrl(normalized.GarageOverlay.Camera, "Garage");
        foreach (var overlay in normalized.AdditionalOverlays) ValidateCameraUrl(overlay.Camera, overlay.Camera.Name);
        return normalized;
    }

    private static void ValidateCameraUrl(CameraSettings camera, string label)
    {
        if (string.IsNullOrWhiteSpace(camera.RtspUrl)) return;
        if (!Uri.TryCreate(camera.RtspUrl, UriKind.Absolute, out var uri) || !uri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{label} does not contain a valid RTSP URL.");
    }

    private static async Task WriteAsync(string path, AppSettings settings, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static bool IsSharingViolation(IOException error) => (error.HResult & 0xffff) is 32 or 33;
}
