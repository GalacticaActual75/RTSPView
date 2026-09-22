# Find an ONVIF camera stream

In **Streams**, add or edit a stream and select **Find ONVIF stream** beside
Source URL. In the dialog:

1. Select **Find cameras on host network**, then choose a discovered camera.
   Discovery runs on the Windows RTSPView host, not on the browser's computer.
2. Alternatively, enter an IP address, hostname and port, or the camera's full
   HTTP/HTTPS ONVIF device-service URL. The default path is `/onvif/device_service`.
3. Enter the camera's ONVIF username and password, then **Load stream profiles**.
   Some cameras require ONVIF to be enabled and a separate ONVIF account created.
4. Choose the main stream or a smaller substream and select **Use selected stream**.
5. **Save & apply**, then assign the stream to a tile under **Layouts** and apply
   the layout to show it on the wall. Discovery/profile selection alone does not start playback.

The selected RTSP address and its credentials use the existing stream settings
storage and export rules. The separate ONVIF password field is cleared when the
dialog closes; credentials are retained in the RTSP address when saved. Use a
camera account with live-view access. Names and stream profiles come from the camera.

## Coverage and limits

- IPv4 WS-Discovery on active multicast-capable interfaces, bounded to four
  seconds per interface, eight interfaces and 64 discovered entries. Discovery
  normally stays within the local subnet; routers, VLANs and firewalls may block
  it. Manual camera addresses work without multicast discovery.
- ONVIF Media and Media2 profile selection, legacy GetCapabilities fallback,
  WS-Security UsernameToken digest, HTTP Digest authentication, and camera-clock
  adjustment for authentication. HTTPS requires a trusted camera certificate.
- Live RTSP unicast playback through the existing player. This is camera/stream
  setup support, not ONVIF certification. PTZ, events, recording search and camera
  configuration are not included. IPv6 multicast discovery is not included.
- No port scan or guessed RTSP paths. RTSP-only devices need a manual URL.
- Advertised service/stream hosts must match the entered camera host (wildcard
  addresses are replaced). If a camera advertises an IP when entered by hostname,
  use that IP. Alternate-host gateways need a manually confirmed RTSP URL.

Automated checks cover UDP probe/replies, Media/Media2 SOAP exchanges, both
authentication methods, clock skew, HTTP admin/CSRF protection, profile selection,
credential encoding, invalid responses and actual H.264 decoding from a simulated
ONVIF/RTSP camera. **Physical camera/firmware verification is pending.**

Protocol references: [ONVIF device service](https://www.onvif.org/ver10/device/wsdl/devicemgmt.wsdl),
[Media](https://www.onvif.org/ver10/media/wsdl/media.wsdl),
[Media2](https://www.onvif.org/ver20/media/wsdl/media.wsdl).
