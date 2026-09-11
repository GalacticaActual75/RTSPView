using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace SpotMonitor.Controller;

public sealed class LanAccessService
{
    private readonly string _path;
    private readonly Func<Task> _configureFirewall;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly int _port;
    private volatile bool _enabled;
    public bool Managed { get; }
    public bool Enabled => Managed && _enabled;
    public IConfigurationRoot ListenerConfiguration { get; }

    public LanAccessService(string dataDirectory, bool managed, Func<Task> configureFirewall, int port = 5080)
    {
        (_path, Managed, _configureFirewall, _port) = (Path.Combine(dataDirectory, "lan-access.json"), managed, configureFirewall, port);
        try { if (File.Exists(_path)) _enabled = JsonSerializer.Deserialize<LanAccessRequest>(File.ReadAllText(_path))?.Enabled == true; }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { _enabled = false; }
        ListenerConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Endpoints:Admin:Url"] = ListenerUrl }).Build();
    }
    private string ListenerUrl => $"http://{(Enabled ? "0.0.0.0" : "127.0.0.1")}:{_port}";
    public static void ConfigureHostFiltering(IServiceCollection services) =>
        services.PostConfigure<Microsoft.AspNetCore.HostFiltering.HostFilteringOptions>(options => options.AllowedHosts = ["*"]); // The managed middleware below enforces exact hostnames/IPs.
    public object Status() => new
    {
        enabled = Enabled, managed = Managed, port = _port,
        addresses = LanInterfaces().Select(item => $"http://{item.Address}:{_port}").Distinct().ToArray(),
        localUrl = $"http://127.0.0.1:{_port}",
        message = Managed ? (Enabled ? "LAN access is enabled. HTTP is unencrypted; use only a trusted private network."
            : "Local-only access. Enable LAN access to connect from another device.")
            : "Web bindings are configured externally. Remove custom URL/endpoint settings and restart the Controller to use this switch."
    };
    public async Task SetEnabledAsync(bool enabled)
    {
        if (!Managed) throw new InvalidOperationException("Web bindings are configured externally; this switch is unavailable.");
        if (!await _gate.WaitAsync(0)) throw new InvalidOperationException("A LAN access change is already in progress.");
        try
        {
            if (enabled == Enabled) return;
            if (enabled)
            {
                if (!LanInterfaces().Any()) throw new InvalidOperationException("No active IPv4 LAN connection was found.");
                await _configureFirewall(); // Persist/enable only after host approval and successful firewall setup.
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new LanAccessRequest(enabled)));
            File.Move(temporary, _path, true);
            _enabled = enabled;
        }
        finally { _gate.Release(); }
    }
    // Called after the HTTP response is sent; Kestrel reloads the listener without restarting the Viewer.
    public Task ApplyBindingAsync()
    {
        ListenerConfiguration["Endpoints:Admin:Url"] = ListenerUrl;
        ListenerConfiguration.Reload();
        return Task.CompletedTask;
    }
    public bool HostAllowed(string host) => host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        host is "127.0.0.1" or "[::1]" || Enabled &&
        (host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) ||
         LanInterfaces().Any(item => item.Address.ToString().Equals(host, StringComparison.OrdinalIgnoreCase)));
    public bool RemoteAllowed(IPAddress? remote)
    {
        if (remote is null) return false;
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        if (IPAddress.IsLoopback(remote)) return true;
        if (!Enabled || remote.AddressFamily != AddressFamily.InterNetwork) return false;
        return LanInterfaces().Any(item => SameSubnet(remote, item.Address, item.IPv4Mask));
    }
    public static bool SameSubnet(IPAddress remote, IPAddress local, IPAddress mask)
    {
        var a = remote.GetAddressBytes(); var b = local.GetAddressBytes(); var m = mask.GetAddressBytes();
        return a.Length == 4 && b.Length == 4 && m.Length == 4 && m.Any(value => value != 0) &&
            Enumerable.Range(0, 4).All(i => (a[i] & m[i]) == (b[i] & m[i]));
    }
    private static IEnumerable<UnicastIPAddressInformation> LanInterfaces() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
        .Where(item => item.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(item.Address));

    public static async Task ConfigureFirewallAsync()
    {
        var script = Path.Combine(AppContext.BaseDirectory, "Enable-LanAccess.ps1");
        if (!File.Exists(script)) throw new InvalidOperationException("The LAN setup helper is missing. Install the current RTSPView release.");
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script }) start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows did not start LAN setup.");
            await process.WaitForExitAsync();
            if (process.ExitCode == 2) throw new InvalidOperationException("Set your trusted Windows network connection to Private, then enable LAN access again.");
            if (process.ExitCode != 0) throw new InvalidOperationException("Windows could not configure the LAN firewall rule. LAN access was not enabled.");
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        { throw new InvalidOperationException("Windows approval was cancelled. LAN access was not enabled."); }
    }
}
public sealed record LanAccessRequest(bool Enabled);
