using System.Text.Json;

namespace RTSPView.Controller;

public sealed record AutomationActivityEntry(DateTimeOffset At, string RuleId, string Name, string Kind, string Decision);
public sealed class AutomationActivity
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly List<AutomationActivityEntry> _entries = [];
    private readonly Dictionary<string, string> _last = new();
    public string? Error { get; private set; }
    public AutomationActivity(string path)
    {
        _path = path;
        try { if (File.Exists(path)) _entries.AddRange((JsonSerializer.Deserialize<AutomationActivityEntry[]>(File.ReadAllText(path)) ?? []).TakeLast(200)); }
        catch { Error = "Previous activity could not be loaded."; }
    }
    public AutomationActivityEntry[] Entries { get { lock (_gate) return _entries.AsEnumerable().Reverse().ToArray(); } }
    public DateTimeOffset? LastTrigger(string id) { lock (_gate) return _entries.LastOrDefault(e => e.RuleId == id && e.Kind == "Trigger")?.At; }
    public void ResetTransition(string id, string kind) { lock (_gate) _last.Remove(id + ":" + kind); }
    public void Add(string id, string name, string kind, string decision, bool transitionsOnly = false)
    {
        lock (_gate)
        {
            var key = id + ":" + kind;
            if (_last.GetValueOrDefault(key) == decision && (transitionsOnly || _entries.LastOrDefault(e => e.RuleId == id && e.Kind == kind)?.At > DateTimeOffset.UtcNow.AddSeconds(-10))) return;
            _last[key] = decision;
            static string Clean(string s) => new(s.Where(c => !char.IsControl(c)).Take(160).ToArray());
            _entries.Add(new(DateTimeOffset.UtcNow, Clean(id), Clean(name), Clean(kind), Clean(decision)));
            if (_entries.Count > 200) _entries.RemoveRange(0, _entries.Count - 200);
            try { AutomationPersistence.Write(_path, JsonSerializer.Serialize(_entries)); Error = null; }
            catch { Error = "Activity could not be saved; check available disk space and permissions."; }
        }
    }
}
