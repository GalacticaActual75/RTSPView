using RTSPView.Infrastructure;

namespace RTSPView.Controller;

public static class OnvifEndpoints
{
    public sealed record StreamRequest(string Address, string Username, string Password, string ProfileToken, int MediaVersion);

    public static void MapOnvif(this WebApplication app)
    {
        app.MapPost("/api/onvif/discover", async (CancellationToken cancellationToken) =>
            await Run(async token => Results.Ok(new { devices = await OnvifClient.Discover(token) }), cancellationToken)).RequireAuthorization();
        app.MapPost("/api/onvif/profiles", async (OnvifConnection request, CancellationToken cancellationToken) =>
            await Run(async token =>
            {
                using var camera = new OnvifClient(request);
                return Results.Ok(new { profiles = await camera.GetProfiles(token) });
            }, cancellationToken)).RequireAuthorization();
        app.MapPost("/api/onvif/stream", async (StreamRequest request, HttpContext context, CancellationToken cancellationToken) =>
            await Run(async token =>
            {
                context.Response.Headers.CacheControl = "no-store";
                using var camera = new OnvifClient(new(request.Address, request.Username, request.Password));
                return Results.Ok(new { rtspUrl = await camera.GetStreamUri(request.ProfileToken, request.MediaVersion, token) });
            }, cancellationToken)).RequireAuthorization();
    }

    private static async Task<IResult> Run(Func<CancellationToken, Task<IResult>> action, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { return await action(timeout.Token); }
        catch (OnvifException error) { return Results.BadRequest(new { error = error.Message }); }
        catch (OperationCanceledException) { return Results.BadRequest(new { error = "The camera did not respond in time. Check its address, ONVIF port and network connection." }); }
        catch (HttpRequestException) { return Results.BadRequest(new { error = "Could not connect to the camera's ONVIF service. Check its address, port and HTTPS certificate." }); }
        catch (System.Xml.XmlException) { return Results.BadRequest(new { error = "The camera returned an invalid ONVIF response." }); }
    }
}
