using System.Text.Json;

namespace RTSPView.Controller;

// A prepared journal is undone before services start. Removal is the commit point.
public static class AutomationPersistence
{
    private static readonly string[] Files = new[] { "automation.json", "tapo.json", "settings.json" }
        .SelectMany(f => new[] { f, f + ".bak", f + ".bak1", f + ".bak2", f + ".bak3" }).ToArray();
    public static void Write(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write(json); writer.Flush(); stream.Flush(true);
        }
        File.Move(path + ".tmp", path, true);
    }
    public static void Save(string path, string json, bool damaged = false)
    {
        if (File.Exists(path))
        {
            if (damaged) File.Copy(path, path + ".invalid-" + DateTime.UtcNow.Ticks);
            else
            {
                for (var n = 2; n >= 1; n--) if (File.Exists(path + ".bak" + n)) File.Copy(path + ".bak" + n, path + ".bak" + (n + 1), true);
                File.Copy(path, path + ".bak1", true);
            }
        }
        Write(path, json);
    }
    public static T Load<T>(string path, T empty, Action<T> validate, out bool recovered)
    {
        recovered = false;
        if (!File.Exists(path) && !File.Exists(path + ".bak1")) return empty;
        Exception? failure = null;
        foreach (var candidate in new[] { path, path + ".bak1", path + ".bak2", path + ".bak3" })
        {
            try
            {
                var json = File.ReadAllText(candidate);
                var value = JsonSerializer.Deserialize<T>(json) ?? throw new InvalidDataException();
                validate(value);
                if (candidate != path) { Save(path, json, damaged: true); recovered = true; }
                return value;
            }
            catch (Exception e) when (e is IOException or JsonException or InvalidDataException or ArgumentException) { failure = e; }
        }
        throw new InvalidDataException("No valid automation settings backup is available.", failure);
    }
    public static void Prepare(string directory)
    {
        var journal = Path.Combine(directory, "automation-transaction.json");
        if (File.Exists(journal)) throw new IOException("An automation recovery transaction is pending. Restart the Controller.");
        Write(journal, JsonSerializer.Serialize(Files.ToDictionary(f => f, f => File.Exists(Path.Combine(directory, f)) ? File.ReadAllText(Path.Combine(directory, f)) : null)));
    }
    public static void Commit(string directory) => File.Delete(Path.Combine(directory, "automation-transaction.json"));
    public static void Recover(string directory)
    {
        var journal = Path.Combine(directory, "automation-transaction.json");
        if (!File.Exists(journal)) return;
        var entries = JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(journal)) ?? throw new InvalidDataException("Invalid automation recovery journal.");
        if (entries.Count != Files.Length || Files.Any(f => !entries.ContainsKey(f))) throw new InvalidDataException("Invalid automation recovery journal.");
        foreach (var file in Files)
        {
            var path = Path.Combine(directory, file);
            if (entries[file] is { } json) Write(path, json); else File.Delete(path);
        }
        Commit(directory);
    }
}
