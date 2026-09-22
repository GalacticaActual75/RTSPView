namespace RTSPView.Core;

public sealed record ConnectorCamera(string Id, string Name, string RtspUrl, string Topic);
public sealed record ConnectorBroker(string Host, int Port, bool Tls, string Username, string Password);
public sealed record ConnectorSync(int Version, string InstanceId, ConnectorCamera[] Cameras, ConnectorBroker? Broker);

public static class ConnectorImport
{
    public static AppSettings Merge(AppSettings current, ConnectorSync request)
    {
        if (request.Version != 1 || !Guid.TryParse(request.InstanceId, out _))
            throw new InvalidDataException("Unsupported connector version or identity.");
        if (request.Cameras is null || request.Cameras.Length is < 1 or > 16 || request.Cameras.Any(c => c is null) ||
            request.Cameras.Select(c => c.Id).Distinct().Count() != request.Cameras.Length)
            throw new InvalidDataException("Select 1–16 distinct cameras.");
        var cameras = current.Normalize().Cameras.ToArray();
        var deleted = current.DeletedCameraSlots.ToHashSet();
        var count = current.CameraCount;
        foreach (var incoming in request.Cameras)
        {
            if (string.IsNullOrWhiteSpace(incoming.Id) || incoming.Id.Length > 128 || incoming.Id.Any(char.IsControl) ||
                string.IsNullOrWhiteSpace(incoming.Name) || incoming.Name.Length > 100 || incoming.Name.Any(char.IsControl) ||
                incoming.RtspUrl is null || incoming.RtspUrl.Length > 4096 ||
                !Uri.TryCreate(incoming.RtspUrl, UriKind.Absolute, out var url) || url.Scheme != "rtsp" || string.IsNullOrWhiteSpace(url.Host) ||
                url.IsLoopback || url.Host is "0.0.0.0" or "[::]" ||
                incoming.Topic is null || incoming.Topic.Length > 512 || incoming.Topic.IndexOfAny(['#', '+', '\0']) >= 0)
                throw new InvalidDataException("Invalid camera details. Use a reachable RTSP rebroadcast address and exact MQTT topic.");
            var identity = request.InstanceId + ":" + incoming.Id;
            var matches = cameras.Select((c, i) => (c, i)).Where(x => x.c.ScryptedId == identity).ToArray();
            if (matches.Length > 1) throw new InvalidDataException("Duplicate connector identities in saved cameras.");
            var index = matches.Length == 1 ? matches[0].i : -1;
            if (index < 0)
            {
                var existing = cameras.Select((c, i) => (c, i)).Where(x => x.c.ScryptedId.Length == 0 && x.c.RtspUrl == incoming.RtspUrl).ToArray();
                if (existing.Length > 1) throw new InvalidDataException("Multiple existing streams match an imported URL. Remove the duplicate before syncing.");
                index = existing.Length == 1 ? existing[0].i : Array.FindIndex(cameras, c => string.IsNullOrWhiteSpace(c.RtspUrl) && string.IsNullOrEmpty(c.ScryptedId));
            }
            if (index < 0) throw new InvalidDataException("Not enough free main-stream slots. RTSPView supports 16 main streams.");
            var previous = cameras[index];
            cameras[index] = previous with { Name = incoming.Name, RtspUrl = incoming.RtspUrl, SourceMode = StreamSourceMode.Auto,
                ScryptedId = identity, ScryptedTopic = incoming.Topic,
                Enabled = previous.RtspUrl.Length == 0 || previous.Enabled };
            deleted.Remove(previous.Slot);
            count = Math.Max(count, index + 1);
        }
        return (current with { Cameras = cameras, CameraCount = count, DeletedCameraSlots = deleted.ToArray() }).Normalize();
    }

    public static AutomationSettings BrokerSettings(AutomationSettings current, ConnectorBroker broker)
    {
        if (broker.Host is null || broker.Username is null || broker.Password is null || broker.Password.Length > 1024 ||
            broker.Host is "localhost" or "127.0.0.1" or "::1" or "0.0.0.0" or "::")
            throw new InvalidDataException("Use a broker address reachable from RTSPView.");
        // Pairing does not enable rules or change their actions.
        return current with { Host = broker.Host, Port = broker.Port, Tls = broker.Tls,
            Authenticate = broker.Username.Length > 0, Username = broker.Username };
    }
}
