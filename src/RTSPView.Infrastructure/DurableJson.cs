using System.Text.Json;

namespace RTSPView.Infrastructure;

// Optional host state only. Authentication and credentials retain their fail-closed loaders.
public static class DurableJson
{
    public static T Read<T>(string path, Func<T> fallback, Action<T> validate, Action<string>? warn = null)
    {
        T ReadOne(string file)
        {
            var result = JsonSerializer.Deserialize<T>(File.ReadAllText(file)) ?? throw new InvalidDataException("Empty state.");
            validate(result); return result;
        }
        if (!File.Exists(path) && !File.Exists(path + ".bak")) return fallback();
        try { return ReadOne(path); }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            PreserveInvalid(path);
            try
            {
                var restored = ReadOne(path + ".bak");
                // Recovery is read-only until the caller explicitly saves. Do not overwrite evidence.
                warn?.Invoke("Recovered " + Path.GetFileName(path) + " from its backup.");
                return restored;
            }
            catch (Exception backupError) when (backupError is JsonException or InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException)
            {
                warn?.Invoke(Path.GetFileName(path) + " could not be read; safe defaults are active. Save settings to recover.");
                return fallback();
            }
        }
    }

    public static void PreserveInvalid(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            // Preserve one bounded set of private copies; never print their contents.
            var copy = path + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            File.Copy(path, copy, true);
            foreach (var old in Directory.EnumerateFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".invalid-*").OrderDescending().Skip(3)) File.Delete(old);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    public static void Write<T>(string path, T value, bool backup = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(output, value); output.Flush(true); }
            if (File.Exists(path))
            {
                var valid = false;
                if (backup)
                    try { using var prior = JsonDocument.Parse(File.ReadAllText(path)); valid = prior.RootElement.ValueKind == JsonValueKind.Object; }
                    catch (JsonException) { PreserveInvalid(path); }
                File.Replace(temporary, path, valid ? path + ".bak" : null, true);
            }
            else File.Move(temporary, path);
        }
        finally
        {
            try { File.Delete(temporary); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
