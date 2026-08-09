using System.Text.Json;
using SpotMonitor.Core;

namespace SpotMonitor.Infrastructure;

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
        var temporary = _path + ".tmp";
        await WriteAsync(temporary, normalized, cancellationToken);
        if (File.Exists(_path)) File.Replace(temporary, _path, BackupPath, true);
        else File.Move(temporary, _path);
    }

    public async Task ExportWithoutCredentialsAsync(AppSettings settings, string destination, CancellationToken cancellationToken = default)
    {
        var sanitized = settings.Normalize() with
        {
            Cameras = settings.Cameras.Select(camera => camera with { RtspUrl = RtspUrlSanitizer.RemoveCredentials(camera.RtspUrl) }).ToArray(),
            Camera = settings.Camera with { RtspUrl = RtspUrlSanitizer.RemoveCredentials(settings.Camera.RtspUrl) }
        };
        await WriteAsync(destination, Validate(sanitized), cancellationToken);
    }

    public Task<AppSettings> ImportAsync(string source, CancellationToken cancellationToken = default) => ReadAndValidateAsync(source, cancellationToken);

    private static async Task<AppSettings> ReadAndValidateAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Configuration is empty.");
        return Validate(settings);
    }

    private static AppSettings Validate(AppSettings settings)
    {
        if (settings.SchemaVersion > AppSettings.CurrentSchemaVersion)
            throw new InvalidDataException($"Configuration schema {settings.SchemaVersion} is newer than this application supports.");
        var normalized = settings.Normalize();
        foreach (var camera in normalized.Cameras)
        {
            if (string.IsNullOrWhiteSpace(camera.RtspUrl)) continue;
            if (!Uri.TryCreate(camera.RtspUrl, UriKind.Absolute, out var uri) || !uri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Camera {camera.Slot} does not contain a valid RTSP URL.");
        }
        return normalized;
    }

    private static async Task WriteAsync(string path, AppSettings settings, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
