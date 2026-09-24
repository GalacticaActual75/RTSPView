using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("RTSPView.OnvifChecks")]

namespace RTSPView.Infrastructure;

public sealed record OnvifConnection(string Address, string Username = "", string Password = "");
public sealed record OnvifProfile(string Token, string Name, string Encoding, int Width, int Height, int MediaVersion);
public sealed record OnvifDevice(string Address, string Name);
public sealed class OnvifException(string message) : Exception(message);

// ONVIF only negotiates a stream. The existing RTSP player owns playback and recovery.
public sealed class OnvifClient : IDisposable
{
    private const string Device = "http://www.onvif.org/ver10/device/wsdl";
    private const string Media = "http://www.onvif.org/ver10/media/wsdl";
    private const string Media2 = "http://www.onvif.org/ver20/media/wsdl";
    private static readonly XNamespace Soap = "http://www.w3.org/2003/05/soap-envelope";
    private static readonly XNamespace Schema = "http://www.onvif.org/ver10/schema";
    private static readonly XNamespace Security = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
    private static readonly XNamespace Utility = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";
    private readonly HttpClient _http;
    private readonly OnvifConnection _connection;
    private readonly Uri _device;
    private TimeSpan _clockOffset;

    public OnvifClient(OnvifConnection connection)
    {
        _device = DeviceAddress(connection.Address);
        if ((connection.Username?.Length ?? 0) > 256 || (connection.Password?.Length ?? 0) > 1024)
            throw new OnvifException("Camera credentials are too long.");
        _connection = connection with { Username = connection.Username ?? "", Password = connection.Password ?? "" };
        _http = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false, UseProxy = false,
            Credentials = new NetworkCredential(_connection.Username, _connection.Password)
        }) { Timeout = TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = 1024 * 1024 };
    }

    public static Uri DeviceAddress(string address)
    {
        address = address?.Trim() ?? "";
        if (!address.Contains("://", StringComparison.Ordinal)) address = "http://" + address;
        if (address.Length > 2048 || !Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host) ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.Query.Length != 0)
            throw new OnvifException("Enter a camera address or HTTP/HTTPS ONVIF device-service URL. Enter credentials in the separate fields.");
        return uri.AbsolutePath == "/" ? new UriBuilder(uri) { Path = "/onvif/device_service" }.Uri : uri;
    }

    private Uri ServiceAddress(string? address)
    {
        if (!Uri.TryCreate(_device, address, out var uri) || uri.Scheme is not ("http" or "https") ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.Query.Length != 0)
            throw new OnvifException("The camera returned an invalid media-service address.");
        // Broken cameras sometimes advertise a wildcard address. Do not forward
        // camera credentials to an unrelated host supplied in an ONVIF response.
        if (uri.Host is "0.0.0.0" or "[::]") uri = new UriBuilder(uri) { Host = _device.Host }.Uri;
        if (!uri.Host.Equals(_device.Host, StringComparison.OrdinalIgnoreCase) || (_device.Scheme == "https" && uri.Scheme != "https"))
            throw new OnvifException("The camera advertised a media service on a different host or an insecure connection. Use its advertised camera address and try again.");
        return uri;
    }

    private XElement Header()
    {
        var header = new XElement(Soap + "Header");
        if (_connection.Username.Length == 0) return header;
        var nonce = RandomNumberGenerator.GetBytes(20);
        var created = (DateTimeOffset.UtcNow + _clockOffset).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var digest = SHA1.HashData(nonce.Concat(Encoding.UTF8.GetBytes(created + _connection.Password)).ToArray());
        header.Add(new XElement(Security + "Security", new XAttribute(Soap + "mustUnderstand", "true"),
            new XElement(Security + "UsernameToken",
                new XElement(Security + "Username", _connection.Username),
                new XElement(Security + "Password", new XAttribute("Type", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest"), Convert.ToBase64String(digest)),
                new XElement(Security + "Nonce", new XAttribute("EncodingType", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary"), Convert.ToBase64String(nonce)),
                new XElement(Utility + "Created", created))));
        return header;
    }

    public static XDocument ParseXml(string text)
    {
        using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
        return XDocument.Load(reader);
    }

    private async Task<XDocument> Call(Uri address, XNamespace ns, string operation, CancellationToken token, params XElement[] arguments)
    {
        var envelope = new XElement(Soap + "Envelope", new XAttribute(XNamespace.Xmlns + "s", Soap),
            operation == "GetSystemDateAndTime" ? new XElement(Soap + "Header") : Header(),
            new XElement(Soap + "Body", new XElement(ns + operation, arguments)));
        using var request = new HttpRequestMessage(HttpMethod.Post, address);
        request.Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8);
        request.Content.Headers.ContentType = new("application/soap+xml");
        request.Content.Headers.ContentType.CharSet = "utf-8";
        request.Content.Headers.ContentType.Parameters.Add(new("action", '"' + ns.NamespaceName + '/' + operation + '"'));
        using var response = await _http.SendAsync(request, token);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new OnvifException("Camera authentication failed. Enable ONVIF and check the camera's ONVIF username and password.");
        var text = await response.Content.ReadAsStringAsync(token);
        XDocument document;
        try { document = ParseXml(text); }
        catch (XmlException) { throw new OnvifException("The address did not return an ONVIF response. Check its port and device-service path."); }
        if (document.Descendants().Any(e => e.Name.LocalName == "Fault"))
        {
            if (document.Descendants().Any(e => e.Value.Contains("NotAuthorized", StringComparison.OrdinalIgnoreCase)))
                throw new OnvifException("Camera authentication failed. Check ONVIF credentials and the camera clock.");
            throw new OnvifException($"The camera rejected ONVIF {operation}. Check its ONVIF support and account permissions.");
        }
        if (!response.IsSuccessStatusCode) throw new OnvifException($"Camera request failed (HTTP {(int)response.StatusCode}). Check the ONVIF address and port.");
        if (!document.Descendants(ns + (operation + "Response")).Any())
            throw new OnvifException($"The camera returned an unexpected response to {operation}.");
        return document;
    }

    private async Task SynchronizeClock(CancellationToken token)
    {
        try
        {
            var date = (await Call(_device, Device, "GetSystemDateAndTime", token)).Descendants(Schema + "UTCDateTime").FirstOrDefault();
            if (date is null) return;
            int Part(string name) => int.Parse(date.Descendants(Schema + name).First().Value, CultureInfo.InvariantCulture);
            _clockOffset = new DateTimeOffset(Part("Year"), Part("Month"), Part("Day"), Part("Hour"), Part("Minute"), Part("Second"), TimeSpan.Zero) - DateTimeOffset.UtcNow;
        }
        catch (Exception e) when (e is OnvifException or FormatException or InvalidOperationException or ArgumentOutOfRangeException) { }
    }

    private async Task<List<(Uri Address, int Version)>> Services(CancellationToken token)
    {
        await SynchronizeClock(token);
        var services = new List<(Uri, int)>();
        try
        {
            var response = await Call(_device, Device, "GetServices", token, new XElement(XName.Get("IncludeCapability", Device), false));
            foreach (var service in response.Descendants(XName.Get("Service", Device)))
            {
                var ns = service.Element(XName.Get("Namespace", Device))?.Value;
                if (ns is Media or Media2)
                    services.Add((ServiceAddress(service.Element(XName.Get("XAddr", Device))?.Value), ns == Media2 ? 2 : 1));
            }
        }
        catch (OnvifException) { /* Older Profile S devices only expose GetCapabilities. */ }
        if (services.Count == 0)
        {
            var response = await Call(_device, Device, "GetCapabilities", token, new XElement(XName.Get("Category", Device), "Media"));
            var media = response.Descendants(Schema + "Media").FirstOrDefault();
            if (media is not null) services.Add((ServiceAddress(media.Element(Schema + "XAddr")?.Value), 1));
        }
        if (services.Count == 0) throw new OnvifException("This camera did not advertise an ONVIF media service.");
        return services.Distinct().OrderBy(s => s.Item2).ToList();
    }

    private static List<OnvifProfile> ParseProfiles(XDocument response, int version)
    {
        XNamespace ns = version == 2 ? Media2 : Media;
        return response.Descendants(ns + "Profiles").Take(128).Select(profile =>
        {
            var encoder = profile.Descendants().FirstOrDefault(e => e.Name.LocalName is "VideoEncoderConfiguration" or "VideoEncoder");
            string Field(string name) => encoder?.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value ?? "";
            int Number(string name) => int.TryParse(Field(name), out var value) ? value : 0;
            return new OnvifProfile(profile.Attribute("token")?.Value ?? "", profile.Elements().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value ?? "Camera profile",
                Field("Encoding"), Number("Width"), Number("Height"), version);
        }).Where(p => p.Token.Length is > 0 and <= 1024).ToList();
    }

    public async Task<IReadOnlyList<OnvifProfile>> GetProfiles(CancellationToken token)
    {
        OnvifException? failure = null;
        foreach (var service in await Services(token))
        {
            try
            {
                var profiles = ParseProfiles(await Call(service.Address, service.Version == 2 ? Media2 : Media, "GetProfiles", token), service.Version);
                if (profiles.Count > 0) return profiles;
            }
            catch (OnvifException e) { failure = e; }
        }
        throw failure ?? new OnvifException("The camera returned no stream profiles. Configure a video stream in the camera first.");
    }

    public async Task<string> GetStreamUri(string profileToken, int version, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(profileToken) || profileToken.Length > 1024 || version is not (1 or 2))
            throw new OnvifException("Select a camera profile first.");
        var services = await Services(token);
        var service = services.FirstOrDefault(s => s.Version == version);
        if (service.Address is null) throw new OnvifException("The camera's media service changed. Load its profiles again.");
        XNamespace ns = version == 2 ? Media2 : Media;
        var arguments = version == 2
            ? new[] { new XElement(ns + "Protocol", "RTSP"), new XElement(ns + "ProfileToken", profileToken) }
            : new[] { new XElement(ns + "StreamSetup", new XElement(Schema + "Stream", "RTP-Unicast"), new XElement(Schema + "Transport", new XElement(Schema + "Protocol", "RTSP"))), new XElement(ns + "ProfileToken", profileToken) };
        var response = await Call(service.Address, ns, "GetStreamUri", token, arguments);
        var text = response.Descendants().FirstOrDefault(e => e.Name.LocalName == "Uri")?.Value;
        if (text?.Length > 8192 || !Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "rtsp")
            throw new OnvifException("The camera did not return a supported RTSP stream address.");
        var builder = new UriBuilder(uri);
        if (uri.Host is "0.0.0.0" or "[::]") builder.Host = _device.Host;
        if (!builder.Host.Trim('[', ']').Equals(_device.Host.Trim('[', ']'), StringComparison.OrdinalIgnoreCase))
            throw new OnvifException("The camera returned a stream on a different host. Add that RTSP address manually to confirm its credentials.");
        if (_connection.Username.Length > 0)
        {
            builder.UserName = Uri.EscapeDataString(_connection.Username);
            builder.Password = Uri.EscapeDataString(_connection.Password);
        }
        return builder.Uri.AbsoluteUri;
    }

    public void Dispose() => _http.Dispose();

    public static IReadOnlyList<OnvifDevice> ParseDiscovery(string xml, string messageId)
    {
        var document = ParseXml(xml);
        if (!document.Descendants().Any(e => e.Name.LocalName == "RelatesTo" && e.Value == messageId)) return [];
        var devices = new List<OnvifDevice>();
        foreach (var match in document.Descendants().Where(e => e.Name.LocalName == "ProbeMatch").Take(64))
        {
            var scopes = match.Elements().FirstOrDefault(e => e.Name.LocalName == "Scopes")?.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
            var name = scopes.FirstOrDefault(s => s.StartsWith("onvif://www.onvif.org/name/", StringComparison.Ordinal));
            foreach (var address in (match.Elements().FirstOrDefault(e => e.Name.LocalName == "XAddrs")?.Value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                try { devices.Add(new(DeviceAddress(address).AbsoluteUri, name is null ? "ONVIF camera" : Uri.UnescapeDataString(name[27..]))); }
                catch (OnvifException) { }
            }
        }
        return devices;
    }

    public static async Task<IReadOnlyList<OnvifDevice>> Discover(CancellationToken token)
    {
        var addresses = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.SupportsMulticast && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address).Where(a => a.AddressFamily == AddressFamily.InterNetwork).Distinct().Take(8).ToArray();
        return await DiscoverOnInterfaces(addresses, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 3702), token);
    }

    internal static async Task<IReadOnlyList<OnvifDevice>> DiscoverOnInterfaces(IEnumerable<IPAddress> addresses, IPEndPoint destination, CancellationToken token)
    {
        var found = new System.Collections.Concurrent.ConcurrentDictionary<string, OnvifDevice>();
        await Task.WhenAll(addresses.Select(async address =>
        {
            try
            {
                using var socket = new UdpClient(new IPEndPoint(address, 0));
                socket.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                socket.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
                var id = "urn:uuid:" + Guid.NewGuid();
                var probe = Encoding.UTF8.GetBytes($"<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:a=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\" xmlns:d=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\" xmlns:dn=\"http://www.onvif.org/ver10/network/wsdl\"><s:Header><a:MessageID>{id}</a:MessageID><a:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</a:To><a:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</a:Action></s:Header><s:Body><d:Probe><d:Types>dn:NetworkVideoTransmitter</d:Types></d:Probe></s:Body></s:Envelope>");
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(4));
                for (var i = 0; i < 2; i++) await socket.SendAsync(probe, destination, deadline.Token);
                while (found.Count < 64)
                {
                    var reply = await socket.ReceiveAsync(deadline.Token);
                    try { foreach (var device in ParseDiscovery(Encoding.UTF8.GetString(reply.Buffer), id)) found.TryAdd(device.Address, device); }
                    catch (XmlException) { }
                }
            }
            catch (Exception e) when (e is SocketException or OperationCanceledException) { }
        }));
        token.ThrowIfCancellationRequested();
        return found.Values.OrderBy(d => d.Name).ToArray();
    }
}
