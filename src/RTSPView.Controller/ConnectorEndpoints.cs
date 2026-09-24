using System.Text.Json;
using RTSPView.Core;
using RTSPView.Infrastructure;

namespace RTSPView.Controller;

public static class ConnectorEndpoints
{
    private sealed record PairRequest(string Code, string InstanceId);
    private static async Task<T> ReadAsync<T>(HttpContext context)
    {
        if (!context.Request.HasJsonContentType()) throw new InvalidDataException("Send application/json.");
        using var body = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) > 0)
        {
            if (body.Length + read > 65536) throw new InvalidDataException("Connector request exceeds 64 KB.");
            body.Write(buffer, 0, read);
        }
        return JsonSerializer.Deserialize<T>(body.ToArray(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Missing connector request.");
    }

    public static void MapConnector(this WebApplication app, string directory, SemaphoreSlim gate,
        JsonSettingsStore store, Func<string> sessionVersion, Func<bool> setupRequired)
    {
        var pairing = new ConnectorPairing(directory);
        app.MapGet("/api/connector", async () =>
        {
            await gate.WaitAsync();
            try { return Results.Ok(pairing.Status(sessionVersion())); }
            finally { gate.Release(); }
        }).RequireAuthorization();
        app.MapPost("/api/connector/code", async () =>
        {
            await gate.WaitAsync();
            try { return Results.Ok(pairing.CreateCode(sessionVersion())); }
            finally { gate.Release(); }
        }).RequireAuthorization();
        app.MapDelete("/api/connector", async () =>
        {
            await gate.WaitAsync();
            try { pairing.Revoke(); return Results.Ok(new { paired = false }); }
            finally { gate.Release(); }
        }).RequireAuthorization();

        // Dedicated machine endpoints use one-time pairing codes/bearer tokens, never cookies.
        // Browser Origin requests are rejected; the existing /api CSRF rules remain intact.
        app.MapPost("/connector/v1/{action}", async (string action, HttpContext context, AutomationService automation) =>
        {
            if (setupRequired() || context.Request.Headers.ContainsKey("Origin")) return Results.StatusCode(403);
            if (action is not ("pair" or "sync")) return Results.NotFound();
            await gate.WaitAsync(context.RequestAborted);
            try
            {
                if (action == "pair")
                {
                    var request = await ReadAsync<PairRequest>(context);
                    var token = await pairing.PairAsync(request.Code ?? "", request.InstanceId ?? "", sessionVersion());
                    return token is null ? Results.Unauthorized() : Results.Ok(new { token, version = 1 });
                }
                var authorization = context.Request.Headers.Authorization.ToString();
                if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal)) return Results.Unauthorized();
                var sync = await ReadAsync<ConnectorSync>(context);
                if (!pairing.Authorize(authorization[7..], sync.InstanceId, sessionVersion())) return Results.Unauthorized();
                await using var transaction = await store.BeginWriteAsync(context.RequestAborted);
                var previous = await transaction.LoadAsync();
                var updated = ConnectorImport.Merge(previous, sync);
                var oldAutomation = automation.CurrentSettings;
                var broker = sync.Broker;
                var nextAutomation = broker is null ? oldAutomation : ConnectorImport.BrokerSettings(oldAutomation, broker);
                nextAutomation.Validate(updated, broker is not null);
                if (broker is not null && oldAutomation.Rules.Length > 0 && oldAutomation.Host.Length > 0 &&
                    (oldAutomation.Host != broker.Host || oldAutomation.Port != broker.Port || oldAutomation.Tls != broker.Tls ||
                     oldAutomation.Username != broker.Username))
                    throw new InvalidDataException("Existing rules use a different MQTT connection. Align the broker in Automation before syncing.");
                await transaction.SaveAsync(updated);
                try
                {
                    if (broker is not null) await automation.SaveAsync(new(nextAutomation, broker.Password, broker.Password.Length == 0), CancellationToken.None);
                }
                catch
                {
                    await transaction.SaveAsync(previous with { StorageRevision = updated.StorageRevision });
                    throw;
                }
                return Results.Ok(new { version = 1, imported = sync.Cameras.Length,
                    message = "Streams and topic mappings saved. Configure or enable automation rules in RTSPView." });
            }
            catch (Exception e) when (e is JsonException or InvalidDataException or ArgumentException)
            {
                return Results.BadRequest(new { error = e is InvalidDataException ? e.Message : "Invalid connector request." });
            }
            finally { gate.Release(); }
        }).AllowAnonymous();
    }
}
