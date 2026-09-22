using RTSPView.Infrastructure;
using System.Xml;
using System.Net;
using System.Net.Sockets;
using System.Text;

void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
Check(OnvifClient.DeviceAddress("192.0.2.10:8000").AbsoluteUri == "http://192.0.2.10:8000/onvif/device_service", "Default device path");
Check(OnvifClient.DeviceAddress("https://camera.example/custom").AbsolutePath == "/custom", "Custom path lost");
foreach (var invalid in new[] { "", "file:///private", "http://user:secret@camera.example", "http://camera.example/#fragment" })
{
    try { OnvifClient.DeviceAddress(invalid); throw new Exception("Invalid address accepted"); }
    catch (OnvifException) { }
}
const string probe = """
<s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope" xmlns:a="http://schemas.xmlsoap.org/ws/2004/08/addressing" xmlns:d="http://schemas.xmlsoap.org/ws/2005/04/discovery"><s:Header><a:RelatesTo>urn:uuid:fixture</a:RelatesTo></s:Header><s:Body><d:ProbeMatches><d:ProbeMatch><d:Scopes>onvif://www.onvif.org/name/Front%20door</d:Scopes><d:XAddrs>http://192.0.2.10/onvif/device_service file:///private</d:XAddrs></d:ProbeMatch></d:ProbeMatches></s:Body></s:Envelope>
""";
var devices = OnvifClient.ParseDiscovery(probe, "urn:uuid:fixture");
Check(devices.Count == 1 && devices[0].Name == "Front door", "Discovery name/address parsing");
Check(OnvifClient.ParseDiscovery(probe, "unrelated").Count == 0, "Unrelated discovery reply accepted");
try { OnvifClient.ParseXml("<!DOCTYPE x [<!ENTITY a SYSTEM 'file:///private'>]><x>&a;</x>"); throw new Exception("DTD accepted"); }
catch (XmlException) { }
Console.WriteLine("PASS ONVIF discovery response correlation, device addresses, scope names and XML limits.");
using var responder = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
var replyTask = Task.Run(async () =>
{
    for (var i = 0; i < 2; i++)
    {
        var packet = await responder.ReceiveAsync(deadline.Token);
        var request = OnvifClient.ParseXml(Encoding.UTF8.GetString(packet.Buffer));
        var id = request.Descendants().First(e => e.Name.LocalName == "MessageID").Value;
        Check(request.Descendants().Any(e => e.Name.LocalName == "Types" && e.Value.EndsWith("NetworkVideoTransmitter")), "Missing probe type");
        await responder.SendAsync(Encoding.UTF8.GetBytes("not XML"), packet.RemoteEndPoint, deadline.Token);
        await responder.SendAsync(Encoding.UTF8.GetBytes(probe.Replace("urn:uuid:fixture", id)), packet.RemoteEndPoint, deadline.Token);
    }
});
var discovered = await OnvifClient.DiscoverOnInterfaces([IPAddress.Loopback], (IPEndPoint)responder.Client.LocalEndPoint!, deadline.Token);
await replyTask;
Check(discovered.Count == 1 && discovered[0].Name == "Front door", "UDP discovery or duplicate filtering failed");
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
try { await OnvifClient.DiscoverOnInterfaces([], (IPEndPoint)responder.Client.LocalEndPoint!, cancelled.Token); throw new Exception("Cancellation ignored"); }
catch (OperationCanceledException) { }
Console.WriteLine("PASS UDP discovery probe/reply, malformed-packet tolerance, deduplication, timeout and cancellation.");
