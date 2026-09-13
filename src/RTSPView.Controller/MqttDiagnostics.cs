using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;

namespace RTSPView.Controller;

public sealed record MqttObservation(long Id, DateTimeOffset Received, string Topic, string Payload, bool Retained, int Qos, bool Truncated);
public sealed record MqttDiscoveredTopic(string Topic, string? CameraName, DateTimeOffset LastSeen, bool PersonSeen);

// A separate, read-only five-minute subscription never feeds the automation engine.
// Its memory and lifetime are bounded even when the administrator closes the browser.
public sealed class MqttDiagnostics : IDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<MqttObservation> _messages = new();
    private readonly Dictionary<string, MqttDiscoveredTopic> _topics = new();
    private readonly Dictionary<string, string> _names = new();
    private CancellationTokenSource? _cancel;
    private Task _worker = Task.CompletedTask;
    private string _connection = "Stopped";
    private DateTimeOffset? _until;
    private long _id;

    public object Snapshot()
    {
        lock (_sync) return new { connection = _connection, until = _until,
            messages = _messages.ToArray(), topics = _topics.Values.Select(t => t with {
                CameraName = _names.GetValueOrDefault(t.Topic[..t.Topic.LastIndexOf('/')])
            }).OrderBy(t => t.CameraName ?? t.Topic).ToArray() };
    }

    public async Task StartAsync(MqttClientOptions options, string prefix, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(prefix) || prefix.Length > 400 || prefix.IndexOfAny(['#', '+', '\0']) >= 0)
            throw new InvalidDataException("Enter a topic prefix without wildcards, for example scrypted.");
        await _gate.WaitAsync(token);
        try
        {
            _cancel?.Cancel(); await _worker; _cancel?.Dispose();
            _cancel = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            lock (_sync) { _messages.Clear(); _topics.Clear(); _names.Clear(); _connection = "Connecting"; _until = DateTimeOffset.UtcNow.AddMinutes(5); }
            _worker = RunAsync(options, prefix.TrimEnd('/'), _cancel.Token);
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try { _cancel?.Cancel(); await _worker; }
        finally { _gate.Release(); }
    }

    private async Task RunAsync(MqttClientOptions options, string prefix, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                using var client = new MqttFactory().CreateMqttClient();
                client.ApplicationMessageReceivedAsync += e => { Observe(e.ApplicationMessage, prefix); return Task.CompletedTask; };
                try
                {
                    await client.ConnectAsync(options, token);
                    var result = await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter(prefix + "/#")
                        // Scrypted itself publishes these retained camera descriptions on this broker.
                        .WithTopicFilter("homeassistant/+/+/+/config").Build(), token);
                    if (result.Items.Any(i => (int)i.ResultCode >= 128)) throw new InvalidDataException();
                    lock (_sync) _connection = "Listening";
                    while (client.IsConnected) await Task.Delay(500, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch { /* Do not leak broker credentials or exception connection strings. */ }
                lock (_sync) _connection = "Reconnecting — check connection settings and subscription permissions";
                await Task.Delay(3000, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { lock (_sync) { _connection = "Stopped"; _until = null; } }
    }

    private void Observe(MqttApplicationMessage message, string prefix)
    {
        if (message.Topic.Length > 512) return;
        var bytes = message.PayloadSegment;
        var payload = Encoding.UTF8.GetString(bytes.AsSpan()[..Math.Min(bytes.Count, 8192)]);
        var received = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            // Only extract descriptions for devices belonging to the requested Scrypted prefix.
            if (message.Topic.StartsWith("homeassistant/", StringComparison.Ordinal))
            {
                try
                {
                    using var json = JsonDocument.Parse(payload);
                    var root = json.RootElement;
                    if (!root.TryGetProperty("state_topic", out var state) || state.ValueKind != JsonValueKind.String) return;
                    var topic = state.GetString()!;
                    if (!topic.StartsWith(prefix + "/", StringComparison.Ordinal) || topic.Length > 512) return;
                    if (!(root.TryGetProperty("dev", out var device) || root.TryGetProperty("device", out device)) ||
                        !device.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) return;
                    var key = topic[..topic.LastIndexOf('/')];
                    if (_names.Count < 256 || _names.ContainsKey(key)) _names[key] = (name.GetString() ?? "")[..Math.Min(name.GetString()!.Length, 200)];
                }
                catch (Exception e) when (e is JsonException or InvalidOperationException) { }
                return;
            }
            if (!message.Topic.StartsWith(prefix + "/", StringComparison.Ordinal)) return;
            _messages.Enqueue(new(++_id, received, message.Topic, payload, message.Retain, (int)message.QualityOfServiceLevel, bytes.Count > 8192));
            while (_messages.Count > 200) _messages.Dequeue();
            if (message.Topic.EndsWith("/name", StringComparison.Ordinal) && payload.Length is > 0 and <= 200)
            {
                var key = message.Topic[..message.Topic.LastIndexOf('/')];
                if (_names.Count < 256 || _names.ContainsKey(key)) _names[key] = payload;
            }
            if (!message.Topic.EndsWith("/ObjectDetector", StringComparison.Ordinal)) return;
            var person = false;
            try
            {
                using var json = JsonDocument.Parse(payload);
                person = json.RootElement.TryGetProperty("detections", out var detections) && detections.ValueKind == JsonValueKind.Array &&
                    detections.EnumerateArray().Any(d => d.ValueKind == JsonValueKind.Object && d.TryGetProperty("className", out var c) && c.ValueKind == JsonValueKind.String && c.GetString() == "person");
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException) { }
            if (_topics.Count < 256 || _topics.ContainsKey(message.Topic))
                _topics[message.Topic] = new(message.Topic, null, received, person || (_topics.GetValueOrDefault(message.Topic)?.PersonSeen ?? false));
        }
    }

    public void Dispose() { _cancel?.Cancel(); }
}
