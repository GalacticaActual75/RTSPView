using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RTSPView.Controller;

void Check(bool value, string message) { if (!value) throw new Exception(message); }
var scratch = Path.Combine(Path.GetTempPath(), "RTSPView-LanChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
try
{
    var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
    var declined = new LanAccessService(scratch, true, () => throw new InvalidOperationException("Approval cancelled"), port);
    try { await declined.SetEnabledAsync(true); throw new Exception("Cancellation accepted"); } catch (InvalidOperationException) { }
    Check(!declined.Enabled && !File.Exists(Path.Combine(scratch, "lan-access.json")), "Cancelled approval must leave LAN disabled");
    var calls = 0;
    var network = new LanAccessService(scratch, true, () => { calls++; return Task.CompletedTask; }, port);
    Check(!network.Enabled && !network.RemoteAllowed(IPAddress.Parse("203.0.113.20")), "Default remote access blocked");
    Check(network.RemoteAllowed(IPAddress.Loopback) && !network.HostAllowed("untrusted.example"), "Loopback and host filtering");
    Check(LanAccessService.SameSubnet(IPAddress.Parse("203.0.113.20"), IPAddress.Parse("203.0.113.10"), IPAddress.Parse("255.255.255.0")), "Same subnet allowed");
    Check(!LanAccessService.SameSubnet(IPAddress.Parse("198.51.100.20"), IPAddress.Parse("203.0.113.10"), IPAddress.Parse("255.255.255.0")), "Other subnet blocked");
    Check(!LanAccessService.SameSubnet(IPAddress.Parse("203.0.113.20"), IPAddress.Parse("203.0.113.10"), IPAddress.Any), "Zero mask rejected");
    var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.Configuration["AllowedHosts"] = "old-host.example";
    LanAccessService.ConfigureHostFiltering(builder.Services);
    builder.WebHost.ConfigureKestrel(options => options.Configure(network.ListenerConfiguration, reloadOnChange: true));
    await using var app = builder.Build();
    app.Use(async (context, next) => { if (!network.RemoteAllowed(context.Connection.RemoteIpAddress) || !network.HostAllowed(context.Request.Host.Host)) context.Response.StatusCode = 403; else await next(); });
    app.MapGet("/", () => "ok");
    app.MapPut("/toggle/{enabled:bool}", async (HttpContext context, bool enabled) => { await network.SetEnabledAsync(enabled); context.Response.OnCompleted(network.ApplyBindingAsync); return Results.Ok(); });
    await app.StartAsync();
    using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
    var loopback = $"http://127.0.0.1:{port}";
    var status = System.Text.Json.JsonSerializer.SerializeToElement(network.Status());
    var lan = status.GetProperty("addresses")[0].GetString()!;
    async Task<bool> Available(string url) { try { return (await http.GetAsync(url)).IsSuccessStatusCode; } catch (HttpRequestException) { return false; } catch (TaskCanceledException) { return false; } }
    async Task WaitFor(Func<Task<bool>> condition) { for (var i = 0; i < 100; i++) { if (await condition()) return; await Task.Delay(100); } throw new Exception("Listener did not reach expected state"); }
    Check(await Available(loopback) && !await Available(lan), "Only loopback bound by default");
    Check((await http.PutAsync(loopback + "/toggle/true", null)).IsSuccessStatusCode, "Enable response completes before rebind");
    await WaitFor(async () => await Available(lan));
    Check(calls == 1 && network.Enabled, "Firewall configured before enable");
    var restarted = new LanAccessService(scratch, true, () => Task.CompletedTask, port);
    Check(restarted.Enabled, "LAN enabled state survives restart");
    using (var hostile = new HttpRequestMessage(HttpMethod.Get, loopback)) { hostile.Headers.Host = "untrusted.example"; Check((await http.SendAsync(hostile)).StatusCode == HttpStatusCode.Forbidden, "Host restriction remains after enable"); }
    Check((await http.PutAsync(lan + "/toggle/false", null)).IsSuccessStatusCode, "LAN disable responds to remote caller");
    await WaitFor(async () => await Available(loopback) && !await Available(lan));
    Check(!network.Enabled && calls == 1, "Disable needs no elevation");
    Check(!new LanAccessService(scratch, true, () => Task.CompletedTask, port).Enabled, "Disabled state persists");
    await app.StopAsync();
    var external = new LanAccessService(scratch, false, () => throw new Exception("Must not touch firewall"));
    try { await external.SetEnabledAsync(true); throw new Exception("External bindings overwritten"); } catch (InvalidOperationException) { }
    File.WriteAllText(Path.Combine(scratch, "lan-access.json"), "invalid");
    Check(!new LanAccessService(scratch, true, () => Task.CompletedTask).Enabled, "Corrupt state fails closed");
    Console.WriteLine("LAN checks passed: local default, live enable/disable, response completion, persistence, cancelled approval, host/subnet restrictions and external bindings.");
}
finally { Directory.Delete(scratch, true); }
