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
using RTSPView.Core;
using RTSPView.Infrastructure;
using RTSPView.Controller;
using RTSPView.Hardware;

if (args.FirstOrDefault() == ApplicationRestart.HelperSwitch)
{
    await ApplicationRestart.RunHelperAsync(args);
    return;
}

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
if (network.Managed)
{
    LanAccessService.ConfigureHostFiltering(builder.Services);
    builder.WebHost.ConfigureKestrel(options => options.Configure(network.ListenerConfiguration, reloadOnChange: true));
}
else builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://127.0.0.1:5080");
Directory.CreateDirectory(dataDirectory);
await using (var recovery = await new JsonSettingsStore(Path.Combine(dataDirectory, "settings.json")).BeginWriteAsync())
    AutomationPersistence.Recover(dataDirectory);
var dataProtectionDirectory = Path.Combine(dataDirectory, "data-protection");
Directory.CreateDirectory(dataProtectionDirectory);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionDirectory))
    .SetApplicationName("SpotMonitor.Controller")
    .ProtectKeysWithDpapi();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "RTSPView.Admin";
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
builder.Services.AddSingleton(new MaintenanceClient(Path.Combine(AppContext.BaseDirectory, "..", "Maintenance", "RTSPView.Maintenance.exe")));
builder.Services.AddSingleton(provider => new TemperatureMonitor(dataDirectory, new ServiceTemperatureSensors(provider.GetRequiredService<MaintenanceClient>()),
    message => provider.GetRequiredService<RollingFileLogger>().Write("TEMPERATURE", message)));
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService(provider => provider.GetRequiredService<TemperatureMonitor>());
builder.Services.AddSingleton(new WeatherService(dataDirectory));
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService(provider => provider.GetRequiredService<WeatherService>());
builder.Services.AddSingleton<ViewerCommandClient>();
builder.Services.AddSingleton(provider => new TapoService(dataDirectory,
    provider.GetRequiredService<IDataProtectionProvider>(), provider.GetRequiredService<ViewerCommandClient>()));
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService(provider => provider.GetRequiredService<TapoService>());
builder.Services.AddSingleton(new ViewerRuntimeState(dataDirectory));
builder.Services.AddSingleton<ViewerLauncher>();
builder.Services.AddSingleton(provider => new AutomationService(dataDirectory,
    provider.GetRequiredService<IDataProtectionProvider>(), provider.GetRequiredService<ViewerCommandClient>()));
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService(provider => provider.GetRequiredService<AutomationService>());
builder.Services.AddSingleton(new RollingFileLogger(Path.Combine(dataDirectory, "logs")));
builder.Services.AddSingleton(provider => new UpdateService(dataDirectory, provider.GetRequiredService<RollingFileLogger>(),
    builder.Environment.IsEnvironment("Testing") ? null : provider.GetRequiredService<MaintenanceClient>()));
builder.Services.AddSingleton(provider => new PawnIoInstaller(provider.GetRequiredService<MaintenanceClient>(), provider.GetRequiredService<TemperatureMonitor>(),
    provider.GetRequiredService<UpdateService>(), !builder.Environment.IsEnvironment("Testing")));
builder.Services.AddSingleton(provider =>
{
    var service = provider.GetRequiredService<UpdateService>();
    return new UpdateMonitor(dataDirectory, service.InitialStatus, service.CheckAsync, service.StageAndLaunchAsync,
        message => provider.GetRequiredService<RollingFileLogger>().Write("UPDATE", message));
});
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService(provider => provider.GetRequiredService<UpdateMonitor>());
    builder.Services.AddHostedService<WallUpdateServer>();
}
builder.Services.AddSingleton(provider => new RestartScheduler(dataDirectory, async (action, token) =>
{
    if (action == "viewer")
    {
        var runtime = provider.GetRequiredService<ViewerRuntimeState>();
        using var gate = await runtime.AcquireAsync(token);
        if (runtime.Paused) return "Viewer intentionally stopped; scheduled viewer restart skipped.";
        var result = await provider.GetRequiredService<ViewerCommandClient>().SendAsync(ViewerCommandType.RestartViewer, null, token);
        return result.Success ? "Viewer accepted the scheduled restart." : "Viewer restart failed: " + result.Message;
    }
    using var process = Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0 /d p:0:0 /c \"RTSPView scheduled host restart\"")
        { UseShellExecute = false, CreateNoWindow = true }) ?? throw new InvalidOperationException("Windows did not start the restart command.");
    await process.WaitForExitAsync(token);
    return process.ExitCode == 0 ? "Windows accepted the scheduled host restart." : "Windows rejected the scheduled restart. Check host permissions.";
}, provider.GetRequiredService<UpdateService>().TryRunMaintenanceAsync,
    message => provider.GetRequiredService<RollingFileLogger>().Write("SCHEDULE", message)));
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService(provider => provider.GetRequiredService<RestartScheduler>());
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService<ViewerSupervisor>();

var app = builder.Build();
var allowedHosts = (builder.Configuration["AllowedHosts"] ?? "localhost;127.0.0.1;[::1]")
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var startedAt = DateTimeOffset.UtcNow;
var settingsStore = new JsonSettingsStore(Path.Combine(dataDirectory, "settings.json"));
var security = await WebSecurity.LoadOrCreateAsync(Path.Combine(dataDirectory, "web-security.json"), Path.Combine(dataDirectory, "initial-admin-password.txt"));
var configGate = new SemaphoreSlim(1, 1);
var viewerTelemetry = app.Services.GetRequiredService<ViewerTelemetryClient>();
viewerTelemetry.SnapshotReceived += snapshot =>
{
    if (snapshot.Automation is not { } presentation) return;
    var mqtt = app.Services.GetRequiredService<AutomationService>();
    var tapo = app.Services.GetRequiredService<TapoService>();
    foreach (var state in presentation.Rules.GroupBy(r => (r.Source, r.RuleId)))
    {
        var entry = state.FirstOrDefault(r => r.Effective) ?? state.First();
        var history = entry.Source == "MQTT" ? mqtt.Activity : tapo.Activity;
        var name = entry.Source == "MQTT" ? mqtt.CurrentSettings.Rules.FirstOrDefault(r => r.Id == entry.RuleId)?.Name : tapo.CurrentSettings.Rules.FirstOrDefault(r => r.Id == entry.RuleId)?.Name;
        history.Add(entry.RuleId, name ?? "Removed rule", "Presentation", entry.Reason, true);
    }
};
var systemMetrics = app.Services.GetRequiredService<SystemMetricsCollector>();
var temperatures = app.Services.GetRequiredService<TemperatureMonitor>();
var pawnIo = app.Services.GetRequiredService<PawnIoInstaller>();
var viewerCommands = app.Services.GetRequiredService<ViewerCommandClient>();
var auditLog = app.Services.GetRequiredService<RollingFileLogger>();
var updates = app.Services.GetRequiredService<UpdateService>();
var applicationRestartPending = false;
var applicationInstance = Guid.NewGuid().ToString("N");
updates.ExternalMaintenanceActive = async () => applicationRestartPending || (await pawnIo.RemoteStatusAsync()).State is "installing" or "downloading" or "update-installing";
var updateMonitor = app.Services.GetRequiredService<UpdateMonitor>();
var restartScheduler = app.Services.GetRequiredService<RestartScheduler>();
var scheduleTimeZoneId = TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var ianaZone) ? ianaZone : TimeZoneInfo.Local.Id;
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
        var route = (context.GetEndpoint() as Microsoft.AspNetCore.Routing.RouteEndpoint)?.RoutePattern.RawText ?? "unknown route";
        var reference = Guid.NewGuid().ToString("N")[..8];
        auditLog.Write("ERROR", $"Reference {reference}: Request failed: {exception.GetType().Name}; code=0x{exception.HResult:X8}; {context.Request.Method} {route}");
        if (context.Response.HasStarted) throw;
        context.Response.Clear();
        var busySettings = exception is IOException io && JsonSettingsStore.IsSharingViolation(io);
        context.Response.StatusCode = busySettings ? 503 : 500;
        await context.Response.WriteAsJsonAsync(new { error = busySettings
            ? "Settings are busy in another process. Your changes have not been applied; try Apply again."
            : $"Request failed. Reference {reference}. Open Settings → Diagnostics.", reference, panel = "diagnostics" });
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
app.MapConnector(dataDirectory, configGate, settingsStore, () => security.SessionVersion, () => security.PasswordChangeRequired);

app.MapGet("/api/session", (HttpContext context, ClaimsPrincipal user) =>
{
    var token = context.Request.Cookies["RTSPView.Csrf"];
    if (string.IsNullOrWhiteSpace(token) || token.Length != 64)
    {
        token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        context.Response.Cookies.Append("RTSPView.Csrf", token, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps, MaxAge = TimeSpan.FromHours(8) });
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
    logHealth = new { droppedEntries = auditLog.DroppedEntries, error = auditLog.LastError },
    lanAddresses = GetLanAddresses(),
    controllerUptimeSeconds = (long)(DateTimeOffset.UtcNow - startedAt).TotalSeconds,
    windowsUptimeSeconds = (long)TimeSpan.FromMilliseconds(Environment.TickCount64).TotalSeconds,
    processMemoryMb = Math.Round(Process.GetCurrentProcess().WorkingSet64 / 1024d / 1024d, 1),
    currentTime = DateTimeOffset.Now
})).RequireAuthorization();
app.MapGet("/api/telemetry", (ViewerRuntimeState runtime, ViewerLauncher launcher) =>
{
    var viewer = viewerTelemetry.Latest;
    var temperature = temperatures.Status(DateTimeOffset.UtcNow);
    return Results.Ok(new ApplianceTelemetry { ViewerConnected = viewer is not null, Viewer = viewer,
        ViewerRunning = ViewerRuntimeState.IsRunning(), ViewerStarting = launcher.Starting, ViewerPaused = runtime.Paused,
        System = systemMetrics.GetSnapshot() with { CpuTemperatureC = temperature.CpuC, GpuTemperatureC = temperature.GpuC } });
}).RequireAuthorization();
app.MapGet("/api/temperatures", () => Results.Ok(temperatures.Status(DateTimeOffset.UtcNow))).RequireAuthorization();
app.MapGet("/api/dependencies/pawnio", async () => Results.Ok(await pawnIo.StatusAsync())).RequireAuthorization();
app.MapPost("/api/dependencies/pawnio/{action}", async (string action, ConfirmedAction request) =>
{
    if (action is not ("enable-helper" or "install")) return Results.NotFound();
    if (!request.Confirmed) return Results.BadRequest(new { error = "Explicit confirmation is required." });
    if (!await pawnIo.StartAsync(action == "enable-helper")) return Results.Conflict(new { error = "Maintenance is already active or unavailable in this environment." });
    auditLog.Write("AUDIT", "Administrator requested PawnIO " + action + ".");
    return Results.Accepted(value: new { message = action == "enable-helper" ? "Approve Windows maintenance setup on the host." : "PawnIO installation requested. Follow progress below." });
}).RequireAuthorization();
app.MapPut("/api/temperatures", async (TemperatureSettings settings) =>
{
    try { await temperatures.ConfigureAsync(settings); }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
    return Results.Ok(temperatures.Status(DateTimeOffset.UtcNow));
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

app.MapGet("/api/update", async () => Results.Ok(await updateMonitor.StatusAsync())).RequireAuthorization();
app.MapPost("/api/update/check", async (CancellationToken cancellationToken) =>
    Results.Ok(await updateMonitor.CheckAsync(true, DateTimeOffset.UtcNow, cancellationToken))).RequireAuthorization();
app.MapPut("/api/update/notifications", async (WallNotificationSettings request) =>
    Results.Ok(await updateMonitor.NotificationsAsync(request.Enabled))).RequireAuthorization();
app.MapPut("/api/update/channel", async (UpdateChannelRequest request, CancellationToken cancellationToken) =>
{
    try
    {
        await updates.SelectChannelAsync(request.Channel, cancellationToken);
        return Results.Ok(await updateMonitor.CheckAsync(true, DateTimeOffset.UtcNow, cancellationToken));
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
        var reference = Guid.NewGuid().ToString("N")[..8];
        auditLog.Write("ERROR", $"Reference {reference}: Update launch failed ({exception.GetType().Name}).");
        return Results.Json(new { error = $"Unable to start update. Reference {reference}. Open Settings → Updates.", reference, panel = "updates" }, statusCode: 500);
    }
}).RequireAuthorization();

app.MapGet("/api/config", async () => {
    var settings = await settingsStore.LoadAsync();
    var json = System.Text.Json.JsonSerializer.SerializeToNode(settings, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
    json["layoutRevision"] = WallLayoutsRequest.RevisionFor(settings.Layouts, settings.ActiveLayoutId);
    json["automationLayoutRevision"] = WallLayoutsRequest.RevisionFor(settings.AutomationViewLayouts, settings.AutomationViewLayouts[0].Id);
    return Results.Json(json);
}).RequireAuthorization();
app.MapTapo(configGate);
app.MapAutomationPriorities(configGate);
app.MapGet("/api/automation", (AutomationService automation) => Results.Ok(automation.Configuration)).RequireAuthorization();
app.MapGet("/api/automation/status", (AutomationService automation) => Results.Ok(automation.Status)).RequireAuthorization();
app.MapGet("/api/automation/presentation", () => Results.Ok(new { presentation = viewerTelemetry.Latest?.Automation })).RequireAuthorization();
app.MapGet("/api/automation/diagnostics", (HttpContext context, AutomationService automation) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Ok(automation.Diagnostics.Snapshot());
}).RequireAuthorization();
app.MapPost("/api/automation/diagnostics/start", async (MqttDiagnosticsRequest request, AutomationService automation, CancellationToken token) =>
{
    try { await automation.StartDiagnosticsAsync(request, token); }
    catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
    return Results.Ok(automation.Diagnostics.Snapshot());
}).RequireAuthorization();
app.MapPost("/api/automation/diagnostics/stop", async (AutomationService automation, CancellationToken token) =>
{
    await automation.Diagnostics.StopAsync(token);
    return Results.Ok(automation.Diagnostics.Snapshot());
}).RequireAuthorization();
app.MapPut("/api/automation", async (AutomationRequest request, AutomationService automation, CancellationToken token) =>
{
    if (request.Settings is null) return Results.BadRequest(new { error = "Automation settings are required." });
    await configGate.WaitAsync(token);
    try { await automation.SaveAsync(request, token); }
    catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
    finally { configGate.Release(); }
    auditLog.Write("AUTOMATION", "Automation settings saved");
    return Results.Ok(automation.Configuration);
}).RequireAuthorization();
app.MapPost("/api/automation/test", async (AutomationRequest request, AutomationService automation, CancellationToken token) =>
{
    if (request.Settings is null) return Results.BadRequest(new { error = "Automation settings are required." });
    try { return Results.Ok(await automation.TestAsync(request, token)); }
    catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
}).RequireAuthorization();
app.MapPost("/api/automation/rules/{id}/test", async (string id, AutomationRuleTestRequest request, AutomationService automation, CancellationToken token) =>
{
    try
    {
        var result = await automation.TestRuleAsync(id, request.SourceSlot, token);
        auditLog.Write("AUTOMATION", "Local rule test requested");
        return Results.Ok(new { success = result.Success, message = result.Success
            ? "Viewer acknowledged: " + result.Message + " This tests the saved action, not MQTT delivery or zone matching. Priority and clear delay still apply." : result.Message });
    }
    catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
    catch (OperationCanceledException) { return Results.BadRequest(new { error = "Test timed out. Check Controller and Viewer status." }); }
}).RequireAuthorization();
app.MapGet("/api/config/export", async (HttpContext context, AutomationService automation, TapoService tapo) =>
{
    context.Response.Headers.CacheControl = "no-store";
    await configGate.WaitAsync(context.RequestAborted);
    try
    {
        var backup = ConfigurationBackup.Export(await settingsStore.LoadAsync(), automation.CurrentSettings, tapo.CurrentSettings);
        return Results.File(System.Text.Encoding.UTF8.GetBytes(backup.ToJsonString(new JsonSerializerOptions { WriteIndented = true })),
            "application/json", "RTSPView-config.json");
    }
    finally { configGate.Release(); }
}).RequireAuthorization();
app.MapPost("/api/config/import", async (HttpContext context, AutomationService automation, TapoService tapo) =>
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
        var result = await ConfigurationBackup.ImportAsync(System.Text.Encoding.UTF8.GetString(body.ToArray()).TrimStart('\uFEFF'), dataDirectory, settingsStore, automation, tapo, context.RequestAborted);
        var backup = result.Backup;
        auditLog.Write("AUDIT", "Configuration imported from web admin; previous configuration backed up as " + backup);
        return Results.Ok(new { message = result.Message, backup });
    }
    catch (Exception exception) when (exception is InvalidDataException or JsonException)
    {
        return Results.BadRequest(new { error = "Invalid configuration file." });
    }
    finally { configGate.Release(); }
}).RequireAuthorization();
app.MapGet("/api/cameras/{slot:int}/thumbnail", (int slot) =>
{
    if (slot is < 1 or > StreamCatalog.MaximumSlot) return Results.BadRequest(new { error = "Invalid stream slot." });
    if (StreamCatalog.IsOverlaySource(slot)) slot -= 23;
    var path = Path.Combine(dataDirectory, "snapshots", $"camera-{slot}.jpg");
    if (!File.Exists(path)) return Results.NotFound(new { error = "No thumbnail has been captured yet." });
    var capturedAt = File.GetLastWriteTimeUtc(path);
    return Results.File(File.ReadAllBytes(path), "image/jpeg", lastModified: capturedAt);
}).RequireAuthorization();
app.MapPost("/api/cameras/{slot:int}/thumbnail/refresh", async (int slot, CancellationToken cancellationToken) =>
{
    if (slot is < 1 or > StreamCatalog.MaximumSlot) return Results.BadRequest(new { error = "Invalid stream slot." });
    var result = await viewerCommands.SendAsync(ViewerCommandType.CaptureCameraSnapshot, StreamCatalog.IsOverlaySource(slot) ? slot - 23 : slot, cancellationToken);
    return CommandResult(result);
}).RequireAuthorization();

app.MapPost("/api/overlays", async () =>
{
    await configGate.WaitAsync();
    try
    {
        var settings = await settingsStore.LoadAsync();
        var reuse = settings.DeletedOverlaySlots.FirstOrDefault();
        if (reuse != 0)
        {
            var restored = (settings with { DeletedOverlaySlots = settings.DeletedOverlaySlots.Where(s => s != reuse).ToArray() }).Normalize();
            await settingsStore.SaveAsync(restored);
            return Results.Ok(restored.AllOverlays().Single(o => o.Camera.Slot == reuse));
        }
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
        if (index < 0 || index >= settings.AdditionalOverlays.Count || settings.DeletedOverlaySlots.Contains(slot)) return Results.NotFound(new { error = "Overlay no longer exists. Reload the page." });
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

app.MapDelete("/api/overlays/{slot:int}", async (int slot, AutomationService automation, TapoService tapo) =>
{
    await configGate.WaitAsync();
    try
    {
        var settings = await settingsStore.LoadAsync();
        var related = ConfigurationLinks.Stream(settings, automation, tapo, slot, true);
        if (related.Length > 0) return Results.BadRequest(new { error = "This overlay is in use. Remove these references before deleting it.", related });
        await settingsStore.SaveAsync(StreamCatalog.DeleteOverlay(settings, slot));
        try { File.Delete(Path.Combine(dataDirectory, "snapshots", $"camera-{slot}.jpg")); } catch (IOException) { }
        auditLog.Write("AUDIT", $"Overlay {slot} deleted from web admin");
        return Results.Ok(new { deleted = slot });
    }
    catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
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

app.MapPut("/api/snapshots/settings", async (SnapshotSettings snapshots) =>
{
    if (!double.IsFinite(snapshots.IntervalHours) || snapshots.IntervalHours is < 0.1 or > 168)
        return Results.BadRequest(new { error = "Snapshot interval must be between 0.1 and 168 hours." });
    await configGate.WaitAsync();
    try
    {
        var settings = await settingsStore.LoadAsync();
        var updated = (settings with { Snapshots = snapshots }).Normalize();
        await settingsStore.SaveAsync(updated);
        auditLog.Write("AUDIT", "Snapshot refresh settings changed from web admin");
        return Results.Ok(updated.Snapshots);
    }
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
            PreferredMonitorDevice = display.PreferredMonitorDevice,
            RequestHardwareDecoding = display.RequestHardwareDecoding ?? settings.RequestHardwareDecoding,
            HideMouseCursor = display.HideMouseCursor,
            MouseCursorHideSeconds = Math.Clamp(display.MouseCursorHideSeconds, 1, 30),
            ShowCameraNames = display.ShowCameraNames,
            ShowCameraStats = display.ShowCameraStats,
            ShowTileBorders = display.ShowTileBorders,
            DiagnosticsAutoOpenExcludedSlots = display.DiagnosticsAutoOpenExcludedSlots,
            KeepViewerAlwaysOnTop = display.KeepViewerAlwaysOnTop,
            ShowHoverExitButton = display.ShowHoverExitButton
        }).Normalize();
        await settingsStore.SaveAsync(updated);
        auditLog.Write("AUDIT", "Display settings changed from web admin");
        return Results.Ok(new DisplaySettings { StartFullScreen = updated.StartFullScreen, PreferredMonitor = updated.PreferredMonitor, PreferredMonitorDevice = updated.PreferredMonitorDevice, RequestHardwareDecoding = updated.RequestHardwareDecoding, HideMouseCursor = updated.HideMouseCursor, MouseCursorHideSeconds = updated.MouseCursorHideSeconds, ShowCameraNames = updated.ShowCameraNames, ShowCameraStats = updated.ShowCameraStats, ShowTileBorders = updated.ShowTileBorders, DiagnosticsAutoOpenExcludedSlots = updated.DiagnosticsAutoOpenExcludedSlots, KeepViewerAlwaysOnTop = updated.KeepViewerAlwaysOnTop, ShowHoverExitButton = updated.ShowHoverExitButton });
    }
    finally { configGate.Release(); }
}).RequireAuthorization();
app.MapPost("/api/cameras", async () =>
{
    await configGate.WaitAsync();
    try
    {
        var settings = await settingsStore.LoadAsync();
        var reuse = settings.DeletedCameraSlots.FirstOrDefault();
        if (reuse == 0 && settings.CameraCount >= AppSettings.MainCameraSlots.Length)
            return Results.BadRequest(new { error = "The maximum of 16 cameras has been reached." });
        var updated = settings with { CameraCount = reuse == 0 ? settings.CameraCount + 1 : settings.CameraCount, DeletedCameraSlots = settings.DeletedCameraSlots.Where(s => s != reuse).ToArray() };
        await settingsStore.SaveAsync(updated);
        auditLog.Write("AUDIT", $"Camera {updated.CameraCount} added from web admin");
        return Results.Ok(reuse == 0 ? updated.Cameras[updated.CameraCount - 1] : updated.Cameras.Single(c => c.Slot == reuse));
    }
    finally { configGate.Release(); }
}).RequireAuthorization();

app.MapDelete("/api/cameras/{slot:int}", async (int slot, AutomationService automation, TapoService tapo) =>
{
    await configGate.WaitAsync();
    try
    {
        var settings = await settingsStore.LoadAsync();
        var related = ConfigurationLinks.Stream(settings, automation, tapo, slot, false);
        if (related.Length > 0) return Results.BadRequest(new { error = "This stream is in use. Remove these references before deleting it.", related });
        var updated = StreamCatalog.DeleteCamera(settings, slot).Normalize();
        await settingsStore.SaveAsync(updated);
        try { File.Delete(Path.Combine(dataDirectory, "snapshots", $"camera-{slot}.jpg")); } catch (IOException) { }
        auditLog.Write("AUDIT", $"Stream {slot} deleted from web admin");
        return Results.Ok(new { deleted = slot });
    }
    catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
    finally { configGate.Release(); }
}).RequireAuthorization();

app.MapPut("/api/cameras/{slot:int}", async (int slot, CameraSettings camera) =>
{
    if (!AppSettings.MainCameraSlots.Contains(slot) || camera.Slot != slot) return Results.BadRequest(new { error = "Choose a valid main camera ID matching the payload." });
    await configGate.WaitAsync();
    try
    {
        var settings = await settingsStore.LoadAsync();
        if (settings.DeletedCameraSlots.Contains(slot)) return Results.BadRequest(new { error = "This stream was deleted. Use Add stream to restore an available slot." });
        var cameras = settings.Cameras.ToArray();
        var previousCamera = cameras[Array.IndexOf(AppSettings.MainCameraSlots, slot)];
        cameras[Array.IndexOf(AppSettings.MainCameraSlots, slot)] = camera with { Slot = slot,
            ScryptedId = previousCamera.ScryptedId, ScryptedTopic = previousCamera.ScryptedTopic };
        await settingsStore.SaveAsync(settings with { Cameras = cameras });
        auditLog.Write("AUDIT", $"Camera {slot} configuration changed from web admin: {RtspUrlSanitizer.Redact(cameras[Array.IndexOf(AppSettings.MainCameraSlots, slot)].RtspUrl)}");
        return Results.Ok(cameras[Array.IndexOf(AppSettings.MainCameraSlots, slot)]);
    }
    catch (InvalidDataException exception) { return Results.BadRequest(new { error = exception.Message }); }
    finally { configGate.Release(); }
}).RequireAuthorization();

app.MapGet("/api/automation/layouts", async () => Results.Ok(new { layouts = (await settingsStore.LoadAsync()).AutomationViewLayouts })).RequireAuthorization();
app.MapPut("/api/automation/layouts", async (WallLayoutsRequest request, AutomationService automation, TapoService tapo) =>
{
    await configGate.WaitAsync();
    try
    {
        var settings = await settingsStore.LoadAsync();
        if (request.Revision is not null && request.Revision != WallLayoutsRequest.RevisionFor(settings.AutomationViewLayouts, settings.AutomationViewLayouts[0].Id))
            return Results.Conflict(new { error = "Automation layouts changed in another editor. Reload before saving; this draft has not been applied." });
        var automationLayouts = AutomationLayouts.Normalize(request.Layouts);
        if (automationLayouts.Any(l => l.FocusSlots.Length < 2 && (automation.RequiresSecondFocus(l.Id) || tapo.RequiresSecondFocus(l.Id))))
            return Results.BadRequest(new { error = "A rule assigns a Focus 2 camera to this layout. Remove that assignment before removing its second focus tile.", related = ConfigurationLinks.Layouts(automation, tapo, automationLayouts.Where(l => l.FocusSlots.Length < 2).Select(l => l.Id), true, true) });
        if (settings.AutomationViewLayouts.Any(l => !request.Layouts.Any(n => n.Id == l.Id) && (automation.UsesLayout(l.Id) || tapo.UsesAutomationLayout(l.Id))))
            return Results.BadRequest(new { error = "A rule uses this layout. Choose another layout in that rule before deleting it.", related = ConfigurationLinks.Layouts(automation, tapo, settings.AutomationViewLayouts.Where(l => !request.Layouts.Any(n => n.Id == l.Id)).Select(l => l.Id), true) });
        await settingsStore.SaveAsync(settings with { AutomationViewLayouts = automationLayouts });
        auditLog.Write("AUTOMATION", "Automation layouts saved");
        return Results.Ok(new { layouts = automationLayouts, activeLayoutId = automationLayouts[0].Id, revision = WallLayoutsRequest.RevisionFor(automationLayouts, automationLayouts[0].Id) });
    }
    catch (InvalidDataException e) { return Results.BadRequest(new { error = e.Message }); }
    finally { configGate.Release(); }
}).RequireAuthorization();
app.MapPut("/api/layouts", async (WallLayoutsRequest request, TapoService tapo) =>
{
    await configGate.WaitAsync();
    try
    {
        WallLayout.Validate(request.Layouts, request.ActiveLayoutId);
        var settings = await settingsStore.LoadAsync();
        if (request.Revision is not null && request.Revision != WallLayoutsRequest.RevisionFor(settings.Layouts, settings.ActiveLayoutId))
            return Results.Conflict(new { error = "Layouts changed in another editor. Reload before saving; this draft has not been applied." });
        if (settings.Layouts.Any(l => !request.Layouts.Any(n => n.Id == l.Id) && tapo.UsesLayout(l.Id)))
            return Results.BadRequest(new { error = "A Tapo sensor rule uses this layout. Remove its rule reference before deleting it.", related = ConfigurationLinks.Layouts(null, tapo, settings.Layouts.Where(l => !request.Layouts.Any(n => n.Id == l.Id)).Select(l => l.Id), false) });
        var updated = settings with { Layouts = request.Layouts, ActiveLayoutId = request.ActiveLayoutId };
        await settingsStore.SaveAsync(updated);
        auditLog.Write("AUDIT", "Wall layouts saved from web admin");
        return Results.Ok(new { updated.Layouts, updated.ActiveLayoutId, revision = WallLayoutsRequest.RevisionFor(updated.Layouts, updated.ActiveLayoutId) });
    }
    catch (InvalidDataException exception) { return Results.BadRequest(new { error = exception.Message }); }
    finally { configGate.Release(); }
}).RequireAuthorization();

app.MapPost("/api/control/cameras/{slot:int}/restart", async (int slot, CancellationToken cancellationToken) =>
{
    if (slot is < 1 or > StreamCatalog.MaximumSlot) return Results.BadRequest(new { error = "Invalid stream slot." });
    var result = await viewerCommands.SendAsync(ViewerCommandType.RestartCamera, slot, cancellationToken);
    auditLog.Write("AUDIT", $"Remote camera {slot} restart requested: {result.Message}");
    return CommandResult(result);
}).RequireAuthorization();
app.MapPost("/api/control/viewer/{action}", async (string action, ViewerRuntimeState runtime, ViewerLauncher launcher, CancellationToken cancellationToken) =>
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(15));
    if (action.Equals("start", StringComparison.OrdinalIgnoreCase))
    {
        if (pawnIo.Busy || (await pawnIo.StatusAsync()).State is "installing" or "downloading" or "update-installing")
            return Results.Conflict(new { error = "Wait for maintenance to finish before starting the viewer." });
        try { var message = await launcher.StartAsync(timeout.Token); auditLog.Write("AUDIT", message); return Results.Ok(new { success = true, message }); }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or OperationCanceledException)
        { return Results.Problem("Unable to start viewer: " + error.Message, statusCode: 503); }
    }
    using var gate = await runtime.AcquireAsync(timeout.Token);
    if (runtime.Paused) return Results.Conflict(new { error = "Viewer is intentionally stopped. Use Start viewer to resume." });
    var type = action.ToLowerInvariant() switch
    {
        "restart-cameras" => (ViewerCommandType?)ViewerCommandType.RestartAllCameras,
        "restart" => ViewerCommandType.RestartViewer,
        "identify-displays" => ViewerCommandType.IdentifyDisplays,
        "enter-fullscreen" => ViewerCommandType.EnterFullScreen,
        "exit-fullscreen" => ViewerCommandType.ExitFullScreen,
        _ => null
    };
    if (type is null) return Results.NotFound(new { error = "Unknown viewer action." });
    var result = await viewerCommands.SendAsync(type.Value, null, cancellationToken);
    auditLog.Write("AUDIT", $"Remote viewer action '{action}' requested: {result.Message}");
    return CommandResult(result);
}).RequireAuthorization();
async Task<object> RestartScheduleStatus()
{
    var schedules = await restartScheduler.ReadAllAsync();
    return new { schedule = schedules.SelectedAction == "host" ? schedules.Host : schedules.Viewer,
        schedules, timeZone = TimeZoneInfo.Local.DisplayName, timeZoneId = scheduleTimeZoneId, serverTime = DateTimeOffset.UtcNow };
}
app.MapGet("/api/restart-schedule", async () => Results.Ok(await RestartScheduleStatus())).RequireAuthorization();
app.MapPut("/api/restart-schedule", async (RestartScheduleRequest request) =>
{
    if (request.Settings is null) return Results.BadRequest(new { error = "Schedule settings are required." });
    try { await restartScheduler.ConfigureAsync(request.Settings, request.HostAcknowledged, DateTimeOffset.UtcNow); }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
    return Results.Ok(await RestartScheduleStatus());
}).RequireAuthorization();
app.MapPost("/api/restart-schedule/skip", async (string? action) =>
{
    try { await restartScheduler.SkipAsync(DateTimeOffset.UtcNow, action); }
    catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
    auditLog.Write("AUDIT", "Administrator skipped the next scheduled restart.");
    return Results.Ok(await RestartScheduleStatus());
}).RequireAuthorization();

app.MapGet("/api/control/application/status", () => Results.Ok(new { instance = applicationInstance })).RequireAuthorization();
app.MapPost("/api/control/application/restart", async (ConfirmedAction request, HttpContext context, ViewerRuntimeState runtime, CancellationToken token) =>
{
    if (!request.Confirmed) return Results.BadRequest(new { error = "Explicit confirmation is required." });
    if (app.Environment.IsEnvironment("Testing")) return Results.Conflict(new { error = "Application restart is disabled in the test host." });
    try
    {
        var accepted = await updates.TryRunMaintenanceAsync(async () =>
        {
            using var gate = await runtime.AcquireAsync(token);
            if (pawnIo.Busy) throw new InvalidOperationException("Maintenance is active.");
            ApplicationRestart.Prepare(args);
            runtime.Pause();
            applicationRestartPending = true;
        }, token);
        if (!accepted) return Results.Conflict(new { error = "Wait for the current update, maintenance, or application restart to finish." });
        context.Response.OnCompleted(() => { app.Lifetime.StopApplication(); return Task.CompletedTask; });
        auditLog.Write("AUDIT", "Administrator requested an application restart.");
        return Results.Ok(new { success = true, instance = applicationInstance, message = "Restarting application. The dashboard will reconnect shortly." });
    }
    catch (Exception error)
    {
        auditLog.Write("ERROR", "Application restart could not be prepared: " + error.GetType().Name);
        return Results.Problem("Unable to prepare application restart. RTSPView is still running.", statusCode: 503);
    }
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
    return Results.Ok(new { logHealth = new { droppedEntries = auditLog.DroppedEntries, error = auditLog.LastError }, lines = ReadRecentLogLines(Path.Combine(dataDirectory, "logs"), requested).Select(RollingFileLogger.RedactCredentials) });
}).RequireAuthorization();
app.MapGet("/api/logs/download", () =>
{
    var directory = Path.Combine(dataDirectory, "logs");
    var path = Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "rtspview-*.log").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
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
app.MapWeather(settingsStore, configGate);
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
    var cookie = context.Request.Cookies["RTSPView.Csrf"];
    var header = context.Request.Headers["X-CSRF-Token"].ToString();
    if (cookie is null || header.Length != 64 || cookie.Length != 64) return false;
    try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(cookie), Convert.FromHexString(header)); }
    catch (FormatException) { return false; }
}

static string[] ReadRecentLogLines(string directory, int count)
{
    if (!Directory.Exists(directory)) return [];
    var result = new List<string>(count);
    foreach (var path in Directory.EnumerateFiles(directory, "rtspview-*.log").OrderByDescending(File.GetLastWriteTimeUtc))
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
public sealed record WallNotificationSettings(bool Enabled);
