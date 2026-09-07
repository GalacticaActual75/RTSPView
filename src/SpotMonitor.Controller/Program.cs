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
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://0.0.0.0:5080");
var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpotMonitor");
Directory.CreateDirectory(dataDirectory);
var dataProtectionDirectory = Path.Combine(dataDirectory, "data-protection");
Directory.CreateDirectory(dataProtectionDirectory);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionDirectory))
    .SetApplicationName("SpotMonitor.Controller");
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
builder.Services.AddHostedService<ViewerSupervisor>();

var app = builder.Build();
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
app.UseAuthorization();

app.MapGet("/api/session", (HttpContext context, ClaimsPrincipal user) =>
{
    var token = context.Request.Cookies["SpotMonitor.Csrf"];
    if (string.IsNullOrWhiteSpace(token) || token.Length != 64)
    {
        token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        context.Response.Cookies.Append("SpotMonitor.Csrf", token, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps, MaxAge = TimeSpan.FromHours(8) });
    }
    return Results.Ok(new { authenticated = user.Identity?.IsAuthenticated == true, csrfToken = token });
}).AllowAnonymous();
app.MapPost("/api/auth/login", async (HttpContext context, LoginRequest request) =>
{
    var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    if (!loginLimiter.CanAttempt(address, out var retryAfterSeconds)) return Results.Json(new { error = $"Too many failed sign-in attempts. Try again in {retryAfterSeconds} seconds." }, statusCode: StatusCodes.Status429TooManyRequests);
    if (!security.Verify(request.Password ?? string.Empty))
    {
        loginLimiter.RecordFailure(address);
        auditLog.Write("AUDIT", $"Failed web admin sign-in from {address}");
        return Results.Unauthorized();
    }
    loginLimiter.RecordSuccess(address);
    var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    auditLog.Write("AUDIT", $"Web admin sign-in from {address}");
    return Results.Ok(new { authenticated = true });
}).AllowAnonymous();
app.MapPost("/api/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    auditLog.Write("AUDIT", $"Web admin signed out from {context.Connection.RemoteIpAddress}");
    return Results.Ok();
}).RequireAuthorization();

app.MapGet("/api/status", () => Results.Ok(new
{
    version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0",
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
app.MapGet("/api/update", async (CancellationToken cancellationToken) => Results.Ok(await updates.CheckAsync(cancellationToken))).RequireAuthorization();
app.MapPost("/api/update/install", async (ConfirmedAction request, CancellationToken cancellationToken) =>
{
    if (!request.Confirmed) return Results.BadRequest(new { error = "Explicit confirmation is required." });
    try
    {
        var result = await updates.StageAndLaunchAsync(cancellationToken);
        return result.Started ? Results.Ok(result) : Results.BadRequest(new { error = result.Message });
    }
    catch (Exception exception)
    {
        auditLog.Write("ERROR", $"Unable to start SpotMonitor update: {exception.Message}");
        return Results.Problem($"Unable to start update: {exception.Message}", statusCode: 500);
    }
}).RequireAuthorization();

app.MapGet("/api/config", async () => Results.Ok(await settingsStore.LoadAsync())).RequireAuthorization();
app.MapGet("/api/cameras/{slot:int}/thumbnail", (int slot) =>
{
    if (slot is < 1 or > 10) return Results.BadRequest(new { error = "Stream slot must be between 1 and 10." });
    var path = Path.Combine(dataDirectory, "snapshots", $"camera-{slot}.jpg");
    return File.Exists(path) ? Results.File(path, "image/jpeg") : Results.NotFound(new { error = "No thumbnail has been captured yet." });
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
        auditLog.Write("AUDIT", $"Doorbell overlay changed from web admin: host camera {updated.DoorbellOverlay.HostCameraSlot}, {updated.DoorbellOverlay.Position}, {updated.DoorbellOverlay.ViewportShape}, {updated.DoorbellOverlay.SizePercent}%, {updated.DoorbellOverlay.VideoSizing}, offset=({updated.DoorbellOverlay.HorizontalOffsetPercent},{updated.DoorbellOverlay.VerticalOffsetPercent}), zoom={updated.DoorbellOverlay.ZoomPercent}%, {RtspUrlSanitizer.Redact(updated.DoorbellOverlay.Camera.RtspUrl)}");
        return Results.Ok(updated.DoorbellOverlay);
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
app.MapPut("/api/cameras/{slot:int}", async (int slot, CameraSettings camera) =>
{
    if (slot is < 1 or > 9 || camera.Slot != slot) return Results.BadRequest(new { error = "Camera slot must be between 1 and 9 and match the payload." });
    await configGate.WaitAsync();
    try
    {
        var settings = await settingsStore.LoadAsync();
        var cameras = settings.Cameras.ToArray();
        cameras[slot - 1] = camera with { Slot = slot };
        await settingsStore.SaveAsync(settings with { Cameras = cameras });
        auditLog.Write("AUDIT", $"Camera {slot} configuration changed from web admin: {RtspUrlSanitizer.Redact(cameras[slot - 1].RtspUrl)}");
        return Results.Ok(cameras[slot - 1]);
    }
    catch (InvalidDataException exception) { return Results.BadRequest(new { error = exception.Message }); }
    finally { configGate.Release(); }
}).RequireAuthorization();

app.MapPost("/api/cameras/reorder", async (CameraReorderRequest request) =>
{
    if (request.FromSlot is < 1 or > 9 || request.ToSlot is < 1 or > 9)
        return Results.BadRequest(new { error = "Camera positions must be between 1 and 9." });
    if (request.FromSlot == request.ToSlot) return Results.Ok(new { message = "Camera is already in that position." });
    await configGate.WaitAsync();
    try
    {
        var settings = await settingsStore.LoadAsync();
        var cameras = settings.Cameras.ToArray();
        var sourceCamera = cameras[request.FromSlot - 1];
        var destinationCamera = cameras[request.ToSlot - 1];
        cameras[request.FromSlot - 1] = destinationCamera with { Slot = request.FromSlot };
        cameras[request.ToSlot - 1] = sourceCamera with { Slot = request.ToSlot };
        await settingsStore.SaveAsync(settings with { Cameras = cameras });
        foreach (var slot in new[] { request.FromSlot, request.ToSlot })
        {
            var thumbnail = Path.Combine(dataDirectory, "snapshots", $"camera-{slot}.jpg");
            if (File.Exists(thumbnail)) File.Delete(thumbnail);
        }
        auditLog.Write("AUDIT", $"Camera positions {request.FromSlot} and {request.ToSlot} swapped from web admin");
        return Results.Ok(new { message = $"Camera {request.FromSlot} moved to position {request.ToSlot}; the previous camera was swapped back." });
    }
    finally { configGate.Release(); }
}).RequireAuthorization();

app.MapPost("/api/control/cameras/{slot:int}/restart", async (int slot, CancellationToken cancellationToken) =>
{
    if (slot is < 1 or > 10) return Results.BadRequest(new { error = "Stream slot must be between 1 and 10." });
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
        "reboot" => "/r /t 5 /d p:0:0 /c \"SpotMonitor remote administrator request\"",
        _ => null
    };
    if (arguments is null) return Results.NotFound(new { error = "Unknown system action." });
    try
    {
        Process.Start(new ProcessStartInfo("shutdown.exe", arguments) { UseShellExecute = false, CreateNoWindow = true });
        auditLog.Write("AUDIT", $"Remote Windows {action} requested from web admin");
        return Results.Ok(new { success = true, message = $"Windows {action} scheduled in 5 seconds." });
    }
    catch (Exception exception) { return Results.Problem($"Unable to schedule Windows {action}: {exception.Message}", statusCode: 500); }
}).RequireAuthorization();

app.MapGet("/api/logs", (int? lines) =>
{
    var requested = Math.Clamp(lines ?? 400, 50, 2000);
    return Results.Ok(new { lines = ReadRecentLogLines(Path.Combine(dataDirectory, "logs"), requested) });
}).RequireAuthorization();
app.MapGet("/api/logs/download", () =>
{
    var path = Directory.EnumerateFiles(Path.Combine(dataDirectory, "logs"), "spotmonitor-*.log").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    return path is null ? Results.NotFound(new { error = "No log file is available." }) : Results.File(path, "text/plain", Path.GetFileName(path), enableRangeProcessing: true);
}).RequireAuthorization();
app.MapPost("/api/auth/password", async (HttpContext context, PasswordChangeRequest request) =>
{
    if (!security.Verify(request.CurrentPassword ?? string.Empty)) return Results.BadRequest(new { error = "Current password is incorrect." });
    if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 10) return Results.BadRequest(new { error = "New password must contain at least 10 characters." });
    await security.ChangeAsync(request.NewPassword);
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
sealed record PasswordChangeRequest(string? CurrentPassword, string? NewPassword);
sealed record CameraReorderRequest(int FromSlot, int ToSlot);

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

sealed class WebSecurity
{
    private const int Iterations = 210_000;
    private readonly string _settingsPath;
    private readonly string _passwordPath;
    private byte[] _salt;
    private byte[] _hash;
    private readonly object _gate = new();

    private WebSecurity(string settingsPath, string passwordPath, byte[] salt, byte[] hash) { _settingsPath = settingsPath; _passwordPath = passwordPath; _salt = salt; _hash = hash; }
    public bool Verify(string password)
    {
        lock (_gate) return CryptographicOperations.FixedTimeEquals(_hash, Rfc2898DeriveBytes.Pbkdf2(password, _salt, Iterations, HashAlgorithmName.SHA256, _hash.Length));
    }

    public async Task ChangeAsync(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        var temporary = _settingsPath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new SecurityFile(Convert.ToBase64String(salt), Convert.ToBase64String(hash)), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _settingsPath, true);
        lock (_gate) { _salt = salt; _hash = hash; }
        if (File.Exists(_passwordPath)) File.Delete(_passwordPath);
    }

    public static async Task<WebSecurity> LoadOrCreateAsync(string settingsPath, string passwordPath)
    {
        if (File.Exists(settingsPath))
        {
            var saved = JsonSerializer.Deserialize<SecurityFile>(await File.ReadAllTextAsync(settingsPath)) ?? throw new InvalidDataException("Invalid web security settings.");
            return new WebSecurity(settingsPath, passwordPath, Convert.FromBase64String(saved.Salt), Convert.FromBase64String(saved.Hash));
        }
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(10)).ToLowerInvariant();
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        await File.WriteAllTextAsync(settingsPath, JsonSerializer.Serialize(new SecurityFile(Convert.ToBase64String(salt), Convert.ToBase64String(hash)), new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(passwordPath, $"SpotMonitor initial admin password:{Environment.NewLine}{password}{Environment.NewLine}");
        return new WebSecurity(settingsPath, passwordPath, salt, hash);
    }
    private sealed record SecurityFile(string Salt, string Hash);
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
            if (queue.Count < Limit) { retryAfterSeconds = 0; return true; }
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
