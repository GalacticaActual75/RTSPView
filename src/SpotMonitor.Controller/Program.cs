using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using SpotMonitor.Core;
using SpotMonitor.Infrastructure;
using SpotMonitor.Controller;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = "wwwroot"
});
builder.Logging.ClearProviders(); // Use the sanitized application logger; never log raw request URLs or bodies.
var dataDirectory = AppPaths.DataDirectory;
var network = new LanAccessService(dataDirectory,
    string.IsNullOrWhiteSpace(builder.Configuration["urls"]) && !builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any(),
    LanAccessService.ConfigureFirewallAsync);
if (network.Managed) builder.WebHost.ConfigureKestrel(options => options.Configure(network.ListenerConfiguration, reloadOnChange: true));
else builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://127.0.0.1:5080");
Directory.CreateDirectory(dataDirectory);
var dataProtectionDirectory = Path.Combine(dataDirectory, "data-protection");
Directory.CreateDirectory(dataProtectionDirectory);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionDirectory))
    .SetApplicationName("SpotMonitor.Controller")
    .ProtectKeysWithDpapi();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "SpotMonitor.Admin";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new FlexibleRtspTransportConverter()));
builder.Services.AddSingleton<ViewerTelemetryClient>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ViewerTelemetryClient>());
builder.Services.AddSingleton<SystemMetricsCollector>();
builder.Services.AddSingleton<ViewerCommandClient>();
builder.Services.AddSingleton(new RollingFileLogger(Path.Combine(dataDirectory, "logs")));
builder.Services.AddSingleton(provider => new UpdateService(dataDirectory, provider.GetRequiredService<RollingFileLogger>()));
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService<ViewerSupervisor>();

var app = builder.Build();
var allowedHosts = (builder.Configuration["AllowedHosts"] ?? "localhost;127.0.0.1;[::1]")
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var startedAt = DateTimeOffset.UtcNow;
var settingsStore = new JsonSettingsStore(Path.Combine(dataDirectory, "settings.json"));
var security = await WebSecurity.LoadOrCreateAsync(Path.Combine(dataDirectory, "web-security.json"), Path.Combine(dataDirectory, "initial-admin-password.txt"));
var configGate = new SemaphoreSlim(1, 1);
var viewerTelemetry = app.Services.GetRequiredService<ViewerTelemetryClient>();
var systemMetrics = app.Services.GetRequiredService<SystemMetricsCollector>();
var viewerCommands = app.Services.GetRequiredService<ViewerCommandClient>();
var auditLog = app.Services.GetRequiredService<RollingFileLogger>();
var updates = app.Services.GetRequiredService<UpdateService>();
var loginLimiter = new LoginAttemptLimiter();

app.Use(async (context, next) =>
{
    if (network.Managed && (!network.RemoteAllowed(context.Connection.RemoteIpAddress) ||
        security.PasswordChangeRequired && context.Connection.RemoteIpAddress is { } remote && !IPAddress.IsLoopback(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote)))
    {
        context.Response.StatusCode = 403;
        return;
    }
    if (network.Managed ? !network.HostAllowed(context.Request.Host.Host) : !allowedHosts.Contains(context.Request.Host.Host, StringComparer.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 400;
        return;
    }
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    try { await next(); }
    catch (Exception exception)
    {
        auditLog.Write("ERROR", "Request failed: " + exception.GetType().Name);
        if (context.Response.HasStarted) throw;
        context.Response.Clear();
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { error = "Request failed. Check the server logs." });
    }
});
app.UseDefaultFiles();
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
        return Task.CompletedTask;
    });
    await next();
});
app.UseStaticFiles();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && HttpMethods.IsPost(context.Request.Method) ||
        context.Request.Path.StartsWithSegments("/api") && HttpMethods.IsPut(context.Request.Method) ||
        context.Request.Path.StartsWithSegments("/api") && HttpMethods.IsDelete(context.Request.Method))
    {
        if (!IsCsrfValid(context))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing request verification token. Refresh the page and try again." });
            return;
        }
    }
    await next();
});
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        if (context.User.FindFirstValue("session_version") != security.SessionVersion)
        {
            await context.SignOutAsync();
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
        }
        else if (security.PasswordChangeRequired && context.Request.Path.StartsWithSegments("/api") &&
                 context.Request.Path != "/api/session" && context.Request.Path != "/api/auth/password" &&
                 context.Request.Path != "/api/auth/logout")
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { error = "Change your administrator password before continuing.", passwordChangeRequired = true });
            return;
        }
    }
    await next();
});
app.UseAuthorization();

app.MapGet("/api/session", (HttpContext context, ClaimsPrincipal user) =>
{
    var token = context.Request.Cookies["SpotMonitor.Csrf"];
    if (string.IsNullOrWhiteSpace(token) || token.Length != 64)
    {
        token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        context.Response.Cookies.Append("SpotMonitor.Csrf", token, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps, MaxAge = TimeSpan.FromHours(8) });
    }
    return Results.Ok(new { authenticated = user.Identity?.IsAuthenticated == true, csrfToken = token, passwordChangeRequired = user.Identity?.IsAuthenticated == true && security.PasswordChangeRequired });
}).AllowAnonymous();
app.MapPost("/api/auth/login", async (HttpContext context, LoginRequest request) =>
{
    var address = "admin"; // Single account limiter also prevents IP rotation from bypassing lockout.
    if (!loginLimiter.CanAttempt(address, out var retryAfterSeconds)) return Results.Json(new { error = $"Too many failed sign-in attempts. Try again in {retryAfterSeconds} seconds." }, statusCode: StatusCodes.Status429TooManyRequests);
    var sessionVersion = security.SessionVersion;
    if (!security.Verify(request.Password ?? string.Empty))
    {
        auditLog.Write("AUDIT", $"Failed web admin sign-in from {address}");
        return Results.Unauthorized();
    }
    loginLimiter.RecordSuccess(address);
    var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin"), new Claim("session_version", sessionVersion)], CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    auditLog.Write("AUDIT", $"Web admin sign-in from {address}");
    return Results.Ok(new { authenticated = true, passwordChangeRequired = security.PasswordChangeRequired });
}).AllowAnonymous();
app.MapPost("/api/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    auditLog.Write("AUDIT", $"Web admin signed out from {context.Connection.RemoteIpAddress}");
    return Results.Ok();
}).RequireAuthorization();

app.MapGet("/api/status", () => Results.Ok(new
{
    version = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(Program).Assembly)?.InformationalVersion.Split('+')[0] ?? "Development",
    hostname = Environment.MachineName,
    lanAddresses = GetLanAddresses(),
    controllerUptimeSeconds = (long)(DateTimeOffset.UtcNow - startedAt).TotalSeconds,
    windowsUptimeSeconds = (long)TimeSpan.FromMilliseconds(Environment.TickCount64).TotalSeconds,
    processMemoryMb = Math.Round(Process.GetCurrentProcess().WorkingSet64 / 1024d / 1024d, 1),
    currentTime = DateTimeOffset.Now
})).RequireAuthorization();
app.MapGet("/api/telemetry", () =>
{
    var viewer = viewerTelemetry.Latest;
    return Results.Ok(new ApplianceTelemetry { ViewerConnected = viewer is not null, Viewer = viewer, System = systemMetrics.GetSnapshot() });
}).RequireAuthorization();
app.MapGet("/api/network", () => Results.Ok(network.Status())).RequireAuthorization();
app.MapPut("/api/network", async (HttpContext context, LanAccessRequest request) =>
{
    try
    {
        await network.SetEnabledAsync(request.Enabled);
        context.Response.OnCompleted(network.ApplyBindingAsync);
        auditLog.Write("AUDIT", request.Enabled ? "LAN administration enabled." : "LAN administration disabled.");
        return Results.Ok(network.Status());
    }
    catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
    catch (Exception) { return Results.Problem("Unable to save LAN access settings. Check local permissions.", statusCode: 500); }
}).RequireAuthorization();

app.MapGet("/api/update", async (CancellationToken cancellationToken) => Results.Ok(await updates.CheckAsync(cancellationToken))).RequireAuthorization();
app.MapPut("/api/update/channel", async (UpdateChannelRequest request, CancellationToken cancellationToken) =>
{
    try
    {
        await updates.SelectChannelAsync(request.Channel, cancellationToken);
        return Results.Ok(await updates.CheckAsync(cancellationToken));
    }
    catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
}).RequireAuthorization();
app.MapPost("/api/update/install", async (UpdateInstallRequest request, CancellationToken cancellationToken) =>
{
    if (!request.Confirmed) return Results.BadRequest(new { error = "Explicit confirmation is required." });
    try
    {
        var result = await updates.StageAndLaunchAsync(request.Channel, request.Version, cancellationToken);
        return result.Started ? Results.Ok(result) : Results.BadRequest(new { error = result.Message });
    }
    catch (Exception exception)
    {
        auditLog.Write("ERROR", $"Unable to start RTSPView update: {exception.Message}");
        return Results.Problem("Unable to start update. Check server logs.", statusCode: 500);
    }
}).RequireAuthorization();

app.MapGet("/api/config", async () => Results.Ok(await settingsStore.LoadAsync())).RequireAuthorization();
app.MapGet("/api/config/export", async (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    var settings = JsonSettingsStore.WithoutCredentials(await settingsStore.LoadAsync());
    return Results.File(JsonSerializer.SerializeToUtf8Bytes(settings, new JsonSerializerOptions { WriteIndented = true }),
        "application/json", "RTSPView-config.json");
}).RequireAuthorization();
app.MapPost("/api/config/import", async (HttpContext context) =>
{
    const int maximumBytes = 2 * 1024 * 1024;
    using var body = new MemoryStream();
    var buffer = new byte[8192];
    int count;
    while ((count = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) > 0)
    {
        if (body.Length + count > maximumBytes)
            return Results.BadRequest(new { error = "Configuration files must be no larger than 2 MB." });
        body.Write(buffer, 0, count);
    }
    await configGate.WaitAsync(context.RequestAborted);
    try
    {
        var backup = await settingsStore.ImportAndSaveAsync(System.Text.Encoding.UTF8.GetString(body.ToArray()).TrimStart('\uFEFF'), context.RequestAborted);
        auditLog.Write("AUDIT", "Configuration imported from web admin; previous configuration backed up as " + backup);
        return Results.Ok(new { message = "Configuration imported. Applying to the viewer; previous settings were backed up.", backup });
    }
    catch (Exception exception) when (exception is InvalidDataException or JsonException)
    {
        return Results.BadRequest(new { error = "Invalid configuration file." });
    }
    finally { configGate.Release(); }
}).RequireAuthorization();
app.MapGet("/api/cameras/{slot:int}/thumbnail", (int slot) =>
{
    if (slot is < 1 or > AppSettings.MaximumStreamSlot) return Results.BadRequest(new { error = "Invalid stream slot." });
    var path = Path.Combine(dataDirectory, "snapshots", $"camera-{slot}.jpg");
    return File.Exists(path) ? Results.File(path, "image/jpeg") : Results.NotFound(new { error = "No thumbnail has been captured yet." });
}).RequireAuthorization();
app.MapPost("/api/cameras/{slot:int}/thumbnail/refresh", async (int slot, CancellationToken cancellationToken) =>
{
    if (slot is < 1 or > AppSettings.MaximumStreamSlot) return Results.BadRequest(new { error = "Invalid stream slot." });
    var result = await viewerCommands.SendAsync(ViewerCommandType.CaptureCameraSnapshot, slot, cancellationToken);
    return CommandResult(result);
}).RequireAuthorization();

app.MapPost("/api/overlays", async () =>
{
    await configGate.WaitAsync();
    try
    {
        var settings = await settingsStore.LoadAsync();
        if (settings.AdditionalOverlays.Count >= AppSettings.MaximumAdditionalOverlays)
            return Results.BadRequest(new { error = "A maximum of 16 overlays is supported." });
        var count = settings.AdditionalOverlays.Count;
        var overlay = new DoorbellOverlaySettings { Camera = new CameraSettings { Slot = count + 12, Name = $"Overlay {count + 3}", Enabled = false } };
        var updated = (settings with { AdditionalOverlays = settings.AdditionalOverlays.Append(overlay).ToArray() }).Normalize();
        await settingsStore.SaveAsync(updated);
        auditLog.Write("AUDIT", $"Overlay added from web admin: slot {count + 12}");
        return Results.Ok(updated.AdditionalOverlays.Last());
    }
    finally { configGate.Release(); }
}).RequireAuthorization();

app.MapPut("/api/overlays/{slot:int}", async (int slot, DoorbellOverlaySettings overlay) =>
{
    await configGate.WaitAsync();
    try
    {
        var settings = await settingsStore.LoadAsync();
        var index = slot - 12;
        if (index < 0 || index >= settings.AdditionalOverlays.Count) return Results.NotFound(new { error = "Overlay no longer exists. Reload the page." });
        var overlays = settings.AdditionalOverlays.ToArray();
        overlays[index] = overlay with { Camera = (overlay.Camera ?? new CameraSettings()) with { Slot = slot } };
        var updated = (settings with { AdditionalOverlays = overlays }).Normalize();
        await settingsStore.SaveAsync(updated);
        auditLog.Write("AUDIT", $"Overlay changed from web admin: slot {slot}");
        return Results.Ok(updated.AdditionalOverlays[index]);
    }
    catch (InvalidDataException exception) { return Results.BadRequest(new { error = exception.Message }); }
    finally { configGate.Release(); }
}).RequireAuthorization();

app.MapPut("/api/doorbell", async (DoorbellOverlaySettings overlay) =>
{
    await configGate.WaitAsync();
    try
    {
        var settings = await settingsStore.LoadAsync();
        var requested = overlay with { Camera = (overlay.Camera ?? AppSettings.CreateDoorbellCamera()) with { Slot = 10 } };
        var updated = (settings with { DoorbellOverlay = requested }).Normalize();
        await settingsStore.SaveAsync(updated);
        auditLog.Write("AUDIT", $"Doorbell overlay changed from web admin: host camera {updated.DoorbellOverlay.HostCameraSlot}, {updated.DoorbellOverlay.ViewportShape}, viewport={updated.DoorbellOverlay.ViewportWidthPercent}x{updated.DoorbellOverlay.ViewportHeightPercent}% at ({updated.DoorbellOverlay.ViewportHorizontalPositionPercent},{updated.DoorbellOverlay.ViewportVerticalPositionPercent}), aspect-preserving cover, zoom={updated.DoorbellOverlay.ZoomPercent}%, video position=({updated.DoorbellOverlay.ImageHorizontalPositionPercent},{updated.DoorbellOverlay.ImageVerticalPositionPercent}), {RtspUrlSanitizer.Redact(updated.DoorbellOverlay.Camera.RtspUrl)}");
        return Results.Ok(updated.DoorbellOverlay);
    }
    catch (InvalidDataException exception) { return Results.BadRequest(new { error = exception.Message }); }
    finally { configGate.Release(); }
}).RequireAuthorization();

app.MapPut("/api/garage", async (DoorbellOverlaySettings overlay) =>
{
    await configGate.WaitAsync();
    try
    {
        var settings = await settingsStore.LoadAsync();
        var requested = overlay with { Camera = (overlay.Camera ?? AppSettings.CreateGarageCamera()) with { Slot = 11 } };
        var updated = (settings with { GarageOverlay = requested }).Normalize();
        await settingsStore.SaveAsync(updated);
        auditLog.Write("AUDIT", $"Garage overlay changed from web admin: host camera {updated.GarageOverlay.HostCameraSlot}, {updated.GarageOverlay.ViewportShape}, viewport={updated.GarageOverlay.ViewportWidthPercent}x{updated.GarageOverlay.ViewportHeightPercent}% at ({updated.GarageOverlay.ViewportHorizontalPositionPercent},{updated.GarageOverlay.ViewportVerticalPositionPercent}), aspect-preserving cover, zoom={updated.GarageOverlay.ZoomPercent}%, video position=({updated.GarageOverlay.ImageHorizontalPositionPercent},{updated.GarageOverlay.ImageVerticalPositionPercent}), {RtspUrlSanitizer.Redact(updated.GarageOverlay.Camera.RtspUrl)}");
        return Results.Ok(updated.GarageOverlay);
    }
    catch (InvalidDataException exception) { return Results.BadRequest(new { error = exception.Message }); }
    finally { configGate.Release(); }
}).RequireAuthorization();

app.MapPut("/api/display", async (DisplaySettings display) =>
{
    await configGate.WaitAsync();
    try
    {
        var settings = await settingsStore.LoadAsync();
        var updated = (settings with
        {
            StartFullScreen = display.StartFullScreen,
            PreferredMonitor = Math.Max(0, display.PreferredMonitor),
            HideMouseCursor = display.HideMouseCursor,
            MouseCursorHideSeconds = Math.Clamp(display.MouseCursorHideSeconds, 1, 30),
            ShowCameraNames = display.ShowCameraNames,
            ShowCameraStats = display.ShowCameraStats,
            KeepViewerAlwaysOnTop = display.KeepViewerAlwaysOnTop
        }).Normalize();
        await settingsStore.SaveAsync(updated);
        auditLog.Write("AUDIT", "Display settings changed from web admin");
        return Results.Ok(new DisplaySettings { StartFullScreen = updated.StartFullScreen, PreferredMonitor = updated.PreferredMonitor, HideMouseCursor = updated.HideMouseCursor, MouseCursorHideSeconds = updated.MouseCursorHideSeconds, ShowCameraNames = updated.ShowCameraNames, ShowCameraStats = updated.ShowCameraStats, KeepViewerAlwaysOnTop = updated.KeepViewerAlwaysOnTop });
    }
    finally { configGate.Release(); }
}).RequireAuthorization();
app.MapPost("/api/cameras", async () =>
{
    await configGate.WaitAsync();
    try
    {
        var settings = await settingsStore.LoadAsync();
        if (settings.CameraCount >= AppSettings.MainCameraSlots.Length)
            return Results.BadRequest(new { error = "The maximum of 16 cameras has been reached." });
        var updated = settings with { CameraCount = settings.CameraCount + 1 };
        await settingsStore.SaveAsync(updated);
        auditLog.Write("AUDIT", $"Camera {updated.CameraCount} added from web admin");
        return Results.Ok(updated.Cameras[updated.CameraCount - 1]);
    }
    finally { configGate.Release(); }
}).RequireAuthorization();

app.MapPut("/api/cameras/{slot:int}", async (int slot, CameraSettings camera) =>
{
    if (!AppSettings.MainCameraSlots.Contains(slot) || camera.Slot != slot) return Results.BadRequest(new { error = "Choose a valid main camera ID matching the payload." });
    await configGate.WaitAsync();
    try
    {
        var settings = await settingsStore.LoadAsync();
        var cameras = settings.Cameras.ToArray();
        cameras[Array.IndexOf(AppSettings.MainCameraSlots, slot)] = camera with { Slot = slot };
        await settingsStore.SaveAsync(settings with { Cameras = cameras });
        auditLog.Write("AUDIT", $"Camera {slot} configuration changed from web admin: {RtspUrlSanitizer.Redact(cameras[Array.IndexOf(AppSettings.MainCameraSlots, slot)].RtspUrl)}");
        return Results.Ok(cameras[Array.IndexOf(AppSettings.MainCameraSlots, slot)]);
    }
    catch (InvalidDataException exception) { return Results.BadRequest(new { error = exception.Message }); }
    finally { configGate.Release(); }
}).RequireAuthorization();

app.MapPut("/api/layouts", async (WallLayoutsRequest request) =>
{
    await configGate.WaitAsync();
    try
    {
        WallLayout.Validate(request.Layouts, request.ActiveLayoutId);
        var settings = await settingsStore.LoadAsync();
        var updated = settings with { Layouts = request.Layouts, ActiveLayoutId = request.ActiveLayoutId };
        await settingsStore.SaveAsync(updated);
        auditLog.Write("AUDIT", "Wall layouts saved from web admin");
        return Results.Ok(new { updated.Layouts, updated.ActiveLayoutId });
    }
    catch (InvalidDataException exception) { return Results.BadRequest(new { error = exception.Message }); }
    finally { configGate.Release(); }
}).RequireAuthorization();

app.MapPost("/api/control/cameras/{slot:int}/restart", async (int slot, CancellationToken cancellationToken) =>
{
    if (slot is < 1 or > AppSettings.MaximumStreamSlot) return Results.BadRequest(new { error = "Invalid stream slot." });
    var result = await viewerCommands.SendAsync(ViewerCommandType.RestartCamera, slot, cancellationToken);
    auditLog.Write("AUDIT", $"Remote camera {slot} restart requested: {result.Message}");
    return CommandResult(result);
}).RequireAuthorization();
app.MapPost("/api/control/viewer/{action}", async (string action, CancellationToken cancellationToken) =>
{
    var type = action.ToLowerInvariant() switch
    {
        "restart-cameras" => (ViewerCommandType?)ViewerCommandType.RestartAllCameras,
        "restart" => ViewerCommandType.RestartViewer,
        "enter-fullscreen" => ViewerCommandType.EnterFullScreen,
        "exit-fullscreen" => ViewerCommandType.ExitFullScreen,
        _ => null
    };
    if (type is null) return Results.NotFound(new { error = "Unknown viewer action." });
    var result = await viewerCommands.SendAsync(type.Value, null, cancellationToken);
    auditLog.Write("AUDIT", $"Remote viewer action '{action}' requested: {result.Message}");
    return CommandResult(result);
}).RequireAuthorization();
app.MapPost("/api/control/system/{action}", (string action, ConfirmedAction request) =>
{
    if (!request.Confirmed) return Results.BadRequest(new { error = "Explicit confirmation is required." });
    var arguments = action.ToLowerInvariant() switch
    {
        "reboot" => "/r /t 5 /d p:0:0 /c \"RTSPView remote administrator request\"",
        _ => null
    };
    if (arguments is null) return Results.NotFound(new { error = "Unknown system action." });
    try
    {
        Process.Start(new ProcessStartInfo("shutdown.exe", arguments) { UseShellExecute = false, CreateNoWindow = true });
        auditLog.Write("AUDIT", $"Remote Windows {action} requested from web admin");
        return Results.Ok(new { success = true, message = $"Windows {action} scheduled in 5 seconds." });
    }
    catch (Exception exception) { auditLog.Write("ERROR", "System action failed: " + exception.GetType().Name); return Results.Problem("Unable to schedule the system action.", statusCode: 500); }
}).RequireAuthorization();

app.MapGet("/api/logs", (int? lines) =>
{
    var requested = Math.Clamp(lines ?? 400, 50, 2000);
    return Results.Ok(new { lines = ReadRecentLogLines(Path.Combine(dataDirectory, "logs"), requested).Select(RollingFileLogger.RedactCredentials) });
}).RequireAuthorization();
app.MapGet("/api/logs/download", () =>
{
    var path = Directory.EnumerateFiles(Path.Combine(dataDirectory, "logs"), "spotmonitor-*.log").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    return path is null ? Results.NotFound(new { error = "No log file is available." }) : Results.File(
        System.Text.Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, File.ReadLines(path).Select(RollingFileLogger.RedactCredentials))),
        "text/plain", "RTSPView.log");
}).RequireAuthorization();
app.MapPost("/api/auth/password", async (HttpContext context, PasswordChangeRequest request) =>
{
    if (!loginLimiter.CanAttempt("admin", out _)) return Results.StatusCode(429);
    try
    {
        if (!await security.ChangeAsync(request.CurrentPassword ?? string.Empty, request.NewPassword ?? string.Empty))
            return Results.BadRequest(new { error = "Current password is incorrect." });
    }
    catch (PasswordValidationException error) { return Results.BadRequest(new { error = error.Message }); }
    loginLimiter.RecordSuccess("admin");
    auditLog.Write("AUDIT", $"Web administrator password changed from {context.Connection.RemoteIpAddress}");
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok(new { message = "Password changed. Sign in with the new password." });
}).RequireAuthorization();

app.MapFallbackToFile("index.html");
await app.RunAsync();

static string[] GetLanAddresses() => NetworkInterface.GetAllNetworkInterfaces()
    .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
    .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
    .Select(address => address.Address.ToString()).Distinct().ToArray();

static IResult CommandResult(ViewerCommandResult result) => result.Success
    ? Results.Ok(result)
    : Results.Json(new { error = result.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);

static bool IsCsrfValid(HttpContext context)
{
    var cookie = context.Request.Cookies["SpotMonitor.Csrf"];
    var header = context.Request.Headers["X-CSRF-Token"].ToString();
    if (cookie is null || header.Length != 64 || cookie.Length != 64) return false;
    try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(cookie), Convert.FromHexString(header)); }
    catch (FormatException) { return false; }
}

static string[] ReadRecentLogLines(string directory, int count)
{
    if (!Directory.Exists(directory)) return [];
    var result = new List<string>(count);
    foreach (var path in Directory.EnumerateFiles(directory, "spotmonitor-*.log").OrderByDescending(File.GetLastWriteTimeUtc))
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);
        var needed = count - result.Count;
        result.InsertRange(0, lines.TakeLast(needed));
        if (result.Count >= count) break;
    }
    return result.TakeLast(count).ToArray();
}

sealed record LoginRequest(string? Password);
sealed record ConfirmedAction(bool Confirmed);
sealed record UpdateChannelRequest(string Channel);
sealed record UpdateInstallRequest(bool Confirmed, string Channel, string Version);
sealed record PasswordChangeRequest(string? CurrentPassword, string? NewPassword);


sealed class FlexibleRtspTransportConverter : JsonConverter<RtspTransport>
{
    public override RtspTransport Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number) && Enum.IsDefined(typeof(RtspTransport), number))
            return (RtspTransport)number;
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            if (int.TryParse(value, out number) && Enum.IsDefined(typeof(RtspTransport), number)) return (RtspTransport)number;
            if (Enum.TryParse<RtspTransport>(value, true, out var parsed)) return parsed;
        }
        throw new JsonException("Transport must be Auto, TCP, UDP, 0, 1, or 2.");
    }

    public override void Write(Utf8JsonWriter writer, RtspTransport value, JsonSerializerOptions options) => writer.WriteNumberValue((int)value);
}

sealed class LoginAttemptLimiter
{
    private readonly Dictionary<string, Queue<DateTimeOffset>> _failures = new();
    private readonly object _gate = new();
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private const int Limit = 5;

    public bool CanAttempt(string key, out int retryAfterSeconds)
    {
        lock (_gate)
        {
            var queue = GetActive(key);
            if (queue.Count < Limit) { queue.Enqueue(DateTimeOffset.UtcNow); retryAfterSeconds = 0; return true; }
            retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((queue.Peek() + Window - DateTimeOffset.UtcNow).TotalSeconds));
            return false;
        }
    }
    public void RecordFailure(string key) { lock (_gate) GetActive(key).Enqueue(DateTimeOffset.UtcNow); }
    public void RecordSuccess(string key) { lock (_gate) _failures.Remove(key); }
    private Queue<DateTimeOffset> GetActive(string key)
    {
        if (!_failures.TryGetValue(key, out var queue)) _failures[key] = queue = new Queue<DateTimeOffset>();
        while (queue.TryPeek(out var timestamp) && DateTimeOffset.UtcNow - timestamp > Window) queue.Dequeue();
        return queue;
    }
}
