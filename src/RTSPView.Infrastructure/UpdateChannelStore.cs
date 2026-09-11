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
        if (!File.Exists(_path)) return defaultChannel;
        var saved = JsonSerializer.Deserialize<Selection>(File.ReadAllText(_path))
            ?? throw new InvalidDataException("The update channel setting is empty.");
        return UpdateRelease.ValidateChannel(saved.Channel);
    }
    public void Save(string channel)
    {
        UpdateRelease.ValidateChannel(channel);
        Directory.CreateDirectory(dataDirectory);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new Selection(channel)));
        File.Move(temporary, _path, true);
    }
    private sealed record Selection(string Channel);
}
