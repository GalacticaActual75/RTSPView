using RTSPView.Core;

namespace RTSPView.Controller;

public static class TapoEndpoints
{
    public static void MapTapo(this WebApplication app, SemaphoreSlim configGate)
    {
        app.MapGet("/api/tapo", (TapoService tapo) => Results.Ok(tapo.Configuration)).RequireAuthorization();
        app.MapGet("/api/tapo/status", (TapoService tapo) => Results.Ok(tapo.Status)).RequireAuthorization();
        app.MapDelete("/api/tapo/account", async (TapoService tapo, CancellationToken token) =>
        {
            await configGate.WaitAsync(token);
            try { await tapo.RemoveAccountAsync(token); return Results.Ok(tapo.Configuration); }
            finally { configGate.Release(); }
        }).RequireAuthorization();
        app.MapPost("/api/tapo/discover-hubs", async (TapoService tapo, CancellationToken token) =>
        {
            try { return Results.Ok(await tapo.DiscoverHubsAsync(token)); }
            catch (Exception e) when (e is IOException or OperationCanceledException or System.ComponentModel.Win32Exception)
            { return Results.BadRequest(new { error = "Hub discovery could not complete. Check the local network or add an address manually." }); }
        }).RequireAuthorization();
        app.MapPut("/api/tapo", async (TapoRequest request, TapoService tapo, CancellationToken token) =>
        {
            await configGate.WaitAsync(token);
            try { await tapo.SaveAsync(request, token); return Results.Ok(tapo.Configuration); }
            catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
            finally { configGate.Release(); }
        }).RequireAuthorization();
        app.MapPost("/api/tapo/discover", async (TapoRequest request, TapoService tapo, CancellationToken token) =>
        {
            try { return Results.Ok(await tapo.DiscoverAsync(request, token)); }
            catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
            catch (Exception e) when (e is IOException or OperationCanceledException or System.ComponentModel.Win32Exception)
            { return Results.BadRequest(new { error = "Could not read the hubs. Check the addresses, Tapo credentials and Third-Party Compatibility setting." }); }
        }).RequireAuthorization();
        app.MapPost("/api/tapo/rules/{id}/test", async (string id, TapoTestRequest request, TapoService tapo, CancellationToken token) =>
        {
            try { return Results.Ok(await tapo.TestAsync(id, request.State, token)); }
            catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
        }).RequireAuthorization();
    }
}
