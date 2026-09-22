using System.Text.Json;
using RTSPView.Core;

namespace RTSPView.Infrastructure;

// Separate from settings.json so camera imports and installer replacements retain the selection.
public sealed class UpdateChannelStore(string dataDirectory)
{
    private readonly string _path = Path.Combine(dataDirectory, "update-channel.json");
    public string Read(string defaultChannel)
    {
        UpdateRelease.ValidateChannel(defaultChannel);
        return DurableJson.Read(_path, () => new Selection(defaultChannel), value => UpdateRelease.ValidateChannel(value.Channel), message => RecoveryWarning = message).Channel;
    }
    public string? RecoveryWarning { get; private set; }
    public void Save(string channel)
    {
        UpdateRelease.ValidateChannel(channel);
        DurableJson.Write(_path, new Selection(channel));
        RecoveryWarning = null;
    }
    private sealed record Selection(string Channel);
}
