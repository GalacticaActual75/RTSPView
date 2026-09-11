using System.Text.Json;
using RTSPView.Core;

namespace RTSPView.Infrastructure;

public sealed class JsonSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private string BackupPath => _path + ".bak";

    public JsonSettingsStore(string path) => _path = path;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return new AppSettings();
        try
        {
            return await ReadAndValidateAsync(_path, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException)
        {
            if (!File.Exists(BackupPath)) throw new InvalidDataException("The configuration and its backup are unavailable or invalid.", exception);
            var recovered = await ReadAndValidateAsync(BackupPath, cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.Copy(BackupPath, _path, true);
            return recovered;
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var normalized = Validate(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        // Keep the original file beyond the rolling .bak so schema-14 releases can be restored.
        var migrationBackup = _path + ".before-layouts.json";
        if (File.Exists(_path) && !File.Exists(migrationBackup))
        {
            using var original = JsonDocument.Parse(await File.ReadAllTextAsync(_path, cancellationToken));
            if (original.RootElement.TryGetProperty("SchemaVersion", out var schema) && schema.GetInt32() < 15)
                File.Copy(_path, migrationBackup, false);
        }
        var temporary = _path + ".tmp";
        await WriteAsync(temporary, normalized, cancellationToken);
        if (File.Exists(_path)) File.Replace(temporary, _path, BackupPath, true);
        else File.Move(temporary, _path);
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
        await using var stream = File.OpenRead(path);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Configuration is empty.");
        return Validate(settings);
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
}
