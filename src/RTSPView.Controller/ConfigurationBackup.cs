using System.Text.Json;
using System.Text.Json.Nodes;
using RTSPView.Core;
using RTSPView.Infrastructure;

namespace RTSPView.Controller;

public static class ConfigurationBackup
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public static JsonObject Export(AppSettings settings, AutomationSettings mqtt, TapoSettings tapo, bool removeStreamCredentials = true)
    {
        var root = JsonSerializer.SerializeToNode(removeStreamCredentials ? JsonSettingsStore.WithoutCredentials(settings) : settings, Json)!.AsObject();
        // Connection identities, hubs, mappings and rules are configuration; passwords never leave their services.
        root["Automations"] = JsonSerializer.SerializeToNode(new { Version = 1, Mqtt = mqtt, Tapo = tapo }, Json);
        return root;
    }

    public static async Task<(string Backup, string Message)> ImportAsync(string json, string directory,
        JsonSettingsStore store, AutomationService mqtt, TapoService tapo, CancellationToken token)
    {
        var settings = JsonSettingsStore.ParseImport(json);
        using var document = JsonDocument.Parse(json);
        var sections = document.RootElement.EnumerateObject().Where(p => p.Name.Equals("Automations", StringComparison.OrdinalIgnoreCase)).ToArray();
        var before = await store.LoadAsync(token);
        var oldMqtt = mqtt.StoredState;
        var oldTapo = tapo.StoredState;
        AutomationSettings? importedMqtt = null;
        TapoSettings? importedTapo = null;
        var keepMqttPassword = true;
        var keepTapoPassword = true;
        var credentialsNeeded = false;
        if (sections.Length > 0)
        {
            var section = sections[0];
            if (section.Value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid automation backup.");
            var bundle = section.Value.Deserialize<AutomationBundle>(Json) ?? throw new InvalidDataException("Missing automations.");
            if (bundle.Version != 1 || bundle.Mqtt is null || bundle.Tapo is null) throw new InvalidDataException("Unsupported automation backup.");
            importedMqtt = bundle.Mqtt;
            importedTapo = bundle.Tapo;
            // Validate against the imported layouts and cameras before changing any persisted settings.
            importedMqtt.Validate(settings, validateTargets: importedMqtt.Enabled);
            importedTapo.Validate(settings, validateTargets: importedTapo.Enabled);
            keepMqttPassword = oldMqtt.Settings.Host.Equals(importedMqtt.Host, StringComparison.OrdinalIgnoreCase) &&
                oldMqtt.Settings.Port == importedMqtt.Port && oldMqtt.Settings.Tls == importedMqtt.Tls &&
                oldMqtt.Settings.Username == importedMqtt.Username;
            keepTapoPassword = oldTapo.Settings.Username.Equals(importedTapo.Username, StringComparison.OrdinalIgnoreCase);
            if (importedMqtt.Enabled && importedMqtt.Authenticate && (!keepMqttPassword || oldMqtt.ProtectedPassword.Length == 0))
            { importedMqtt = importedMqtt with { Enabled = false }; credentialsNeeded = true; }
            if (importedTapo.Enabled && (!keepTapoPassword || oldTapo.ProtectedPassword.Length == 0))
            { importedTapo = importedTapo with { Enabled = false }; credentialsNeeded = true; }
        }
        var backup = "settings.json.before-import-" + Guid.NewGuid().ToString("N") + ".json";
        await File.WriteAllTextAsync(Path.Combine(directory, backup), Export(before, oldMqtt.Settings, oldTapo.Settings, false).ToJsonString(Json), token);
        AutomationPersistence.Prepare(directory);
        try
        {
            await store.SaveAsync(settings, token);
            if (importedMqtt is not null) await mqtt.SaveAsync(new(importedMqtt, null, ClearPassword: !keepMqttPassword), token);
            if (importedTapo is not null) await tapo.SaveAsync(new(importedTapo, ClearPassword: !keepTapoPassword), token);
            AutomationPersistence.Commit(directory);
        }
        catch
        {
            // Restore encrypted service state too; a failed import must not consume saved credentials.
            await store.SaveAsync(before, CancellationToken.None);
            await mqtt.RestoreStateAsync(oldMqtt);
            await tapo.RestoreStateAsync(oldTapo);
            AutomationPersistence.Recover(directory);
            throw;
        }
        return (backup, "Configuration imported" + (importedMqtt is null ? ". Existing automations were kept." : ", including MQTT and Tapo automations.") +
            (credentialsNeeded ? " Integrations needing passwords were left disabled. Enter their passwords in Automation and enable them." : " Applying to the viewer.") + " Previous configuration was backed up.");
    }

    private sealed record AutomationBundle(int Version, AutomationSettings? Mqtt, TapoSettings? Tapo);
}
