using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RTSPView.Core;

public sealed record AutomationSource(int CameraSlot, string Topic);
public sealed record AutomationRule
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Person overlay";
    public bool Enabled { get; init; } = true;
    public AutomationSource[] Sources { get; init; } = [];
    public int OverlaySlot { get; init; }
    public double ClearMinutes { get; init; } = 2;
}

public sealed record AutomationSettings
{
    public bool Enabled { get; init; }
    public string Host { get; init; } = "";
    public int Port { get; init; } = 1883;
    public bool Tls { get; init; }
    public bool Authenticate { get; init; }
    public string Username { get; init; } = "";
    public string ClientId { get; init; } = "rtspview-" + Guid.NewGuid().ToString("N")[..12];
    public AutomationRule[] Rules { get; init; } = [];

    public void Validate(AppSettings cameras, bool connectionRequired = false)
    {
        if ((Enabled || connectionRequired) && string.IsNullOrWhiteSpace(Host)) throw new InvalidDataException("Enter a broker host.");
        if (Host is null || Host.Length > 253 || Host.Contains('/') || Host.Contains('@') || Host.Any(char.IsWhiteSpace)) throw new InvalidDataException("Use a hostname or IP address without a URL scheme.");
        if (Port is < 1 or > 65535) throw new InvalidDataException("Port must be between 1 and 65535.");
        if (ClientId is null || ClientId.Length is < 1 or > 64 || ClientId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')) throw new InvalidDataException("Client ID must contain 1–64 letters, numbers, dashes or underscores.");
        if (Username is null || Username.Length > 256 || (Authenticate && string.IsNullOrWhiteSpace(Username))) throw new InvalidDataException("Enter a broker username.");
        if (Rules is null || Rules.Length > 32 || Rules.Any(r => r is null) || Rules.Select(r => r.Id).Distinct().Count() != Rules.Length) throw new InvalidDataException("Use at most 32 rules with unique IDs.");
        foreach (var rule in Rules)
        {
            if (!Guid.TryParse(rule.Id, out _) || string.IsNullOrWhiteSpace(rule.Name) || rule.Name.Length > 100) throw new InvalidDataException("Each rule needs an ID and a name of up to 100 characters.");
            if (!double.IsFinite(rule.ClearMinutes) || rule.ClearMinutes is < 0.1 or > 120) throw new InvalidDataException("Clear delay must be 0.1–120 minutes.");
            if (rule.Sources is null || rule.Sources.Length is < 1 or > 32 || rule.Sources.Any(s => s is null) || rule.Sources.Select(s => s.CameraSlot).Distinct().Count() != rule.Sources.Length) throw new InvalidDataException("Select one or more distinct source cameras.");
            foreach (var source in rule.Sources)
            {
                if (string.IsNullOrWhiteSpace(source.Topic) || source.Topic.Length > 512 || source.Topic.IndexOfAny(['#', '+', '\0']) >= 0) throw new InvalidDataException("Enter an exact MQTT event topic without wildcards.");
                if (!cameras.Cameras.Concat(cameras.AllOverlays().Select(o => o.Camera)).Any(c => c.Slot == source.CameraSlot && !string.IsNullOrWhiteSpace(c.RtspUrl))) throw new InvalidDataException("Select a configured source camera.");
            }
            if (!cameras.AllOverlays().Any(o => o.Camera.Slot == rule.OverlaySlot && !string.IsNullOrWhiteSpace(o.Camera.RtspUrl))) throw new InvalidDataException("Select an overlay with a configured RTSP stream.");
        }
    }
}

public sealed record AutomationOverlayLease(string Id, int Slot, DateTimeOffset ExpiresAt);
public sealed record AutomationPresentation(string ConfigurationHash, AutomationOverlayLease[] Leases);

public static class AutomationConfiguration
{
    public static string Hash(AppSettings settings) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings.Normalize()))));
}

// Called by a single worker. Event timestamps, rather than arrival times, control renewal.
public sealed class PersonOverlayEngine
{
    private readonly Dictionary<string, long> _watermarks = new();
    private readonly Dictionary<string, AutomationOverlayLease> _leases = new();
    public IReadOnlyDictionary<string, AutomationOverlayLease> Leases => _leases;
    public void Clear() { _watermarks.Clear(); _leases.Clear(); }
    public void Expire(DateTimeOffset now)
    {
        foreach (var id in _leases.Where(p => p.Value.ExpiresAt <= now).Select(p => p.Key).ToArray()) _leases.Remove(id);
    }
    public string Accept(AutomationSettings settings, string topic, ReadOnlyMemory<byte> payload, bool retained, DateTimeOffset now, DateTimeOffset connectedAt)
    {
        Expire(now);
        if (!settings.Enabled || retained) return "Retained or disabled";
        var rules = settings.Rules.Where(r => r.Enabled && r.Sources.Any(s => s.Topic == topic)).ToArray();
        if (rules.Length == 0) return "Unmapped topic";
        if (payload.Length > 65536) return "Message too large";
        try
        {
            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;
            if (!root.TryGetProperty("timestamp", out var timestamp) || !timestamp.TryGetInt64(out var stamp)) return "Missing timestamp";
            var sourceTime = DateTimeOffset.FromUnixTimeMilliseconds(stamp);
            if (sourceTime < now.AddSeconds(-10) || sourceTime > now.AddSeconds(2) || sourceTime < connectedAt.AddSeconds(-2)) return "Stale event or clock skew";
            if (_watermarks.TryGetValue(topic, out var seen) && stamp <= seen) return "Duplicate or out-of-order event";
            _watermarks[topic] = stamp;
            if (!root.TryGetProperty("detections", out var detections) || detections.ValueKind != JsonValueKind.Array ||
                !detections.EnumerateArray().Any(d => d.ValueKind == JsonValueKind.Object && d.TryGetProperty("className", out var c) && c.ValueKind == JsonValueKind.String && c.GetString() == "person")) return "No person";
            foreach (var rule in rules)
            {
                var id = _leases.TryGetValue(rule.Id, out var existing) ? existing.Id : Guid.NewGuid().ToString("N");
                var expiry = (sourceTime > now ? now : sourceTime).AddMinutes(rule.ClearMinutes);
                if (existing is not null && existing.ExpiresAt > expiry) expiry = existing.ExpiresAt;
                _leases[rule.Id] = new(id, rule.OverlaySlot, expiry);
            }
            return "Person detected";
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentOutOfRangeException) { return "Invalid event"; }
    }
}
