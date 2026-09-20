using System.Security.Cryptography;
using System.Text.Json;
using RTSPView.Core;

namespace RTSPView.Controller;

public sealed record PriorityEntry(string Source, string Id, string Name, int Priority, bool Enabled);
public sealed record PriorityOrder(string Revision, PriorityEntry[] Rules);
public static class AutomationPriorityEndpoints
{
    public static PriorityOrder Snapshot(AutomationSettings mqtt, TapoSettings tapo)
    {
        var revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { mqtt, tapo })));
        var rules = mqtt.Rules.Select(r => new PriorityEntry("MQTT", r.Id, r.Name, r.Priority, mqtt.Enabled && r.Enabled))
            .Concat(tapo.Rules.Select(r => new PriorityEntry("Tapo", r.Id, r.Name, r.Priority, tapo.Enabled && r.Enabled)))
            .OrderBy(r => r.Priority).ThenBy(r => r.Source == "Tapo" ? 0 : 1).ThenBy(r => r.Id, StringComparer.Ordinal).ToArray();
        return new(revision, rules);
    }
    public static void Validate(PriorityOrder request, PriorityOrder current)
    {
        if (request.Revision != current.Revision) throw new InvalidDataException("Rules changed. Reload the priority list before saving.");
        if (request.Rules is null || request.Rules.Any(r => r is null) || request.Rules.Length != current.Rules.Length ||
            request.Rules.Select(r => (r.Source, r.Id)).Distinct().Count() != current.Rules.Length ||
            request.Rules.Any(r => !current.Rules.Any(c => c.Source == r.Source && c.Id == r.Id)))
            throw new InvalidDataException("Include each current automation exactly once.");
    }
    public static void MapAutomationPriorities(this WebApplication app, SemaphoreSlim gate)
    {
        app.MapGet("/api/automation/priorities", async (AutomationService mqtt, TapoService tapo, CancellationToken token) =>
        {
            await gate.WaitAsync(token);
            try { return Results.Ok(Snapshot(mqtt.CurrentSettings, tapo.CurrentSettings)); }
            finally { gate.Release(); }
        }).RequireAuthorization();
        app.MapPut("/api/automation/priorities", async (PriorityOrder request, AutomationService mqtt, TapoService tapo, CancellationToken token) =>
        {
            await gate.WaitAsync(token);
            try
            {
                var beforeMqtt = mqtt.CurrentSettings; var beforeTapo = tapo.CurrentSettings;
                Validate(request, Snapshot(beforeMqtt, beforeTapo));
                var priorities = request.Rules.Select((r, i) => (r.Source, r.Id, Priority: i + 1)).ToDictionary(r => (r.Source, r.Id), r => r.Priority);
                var updatedMqtt = beforeMqtt with { Rules = beforeMqtt.Rules.Select(r => r with { Priority = priorities[("MQTT", r.Id)] }).ToArray() };
                var updatedTapo = beforeTapo with { Rules = beforeTapo.Rules.Select(r => r with { Priority = priorities[("Tapo", r.Id)] }).ToArray() };
                await mqtt.SaveAsync(new(updatedMqtt, null), token);
                try { await tapo.SaveAsync(new(updatedTapo), CancellationToken.None); }
                catch { await mqtt.SaveAsync(new(beforeMqtt, null), CancellationToken.None); throw; }
                return Results.Ok(Snapshot(mqtt.CurrentSettings, tapo.CurrentSettings));
            }
            catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
            finally { gate.Release(); }
        }).RequireAuthorization();
    }
}
