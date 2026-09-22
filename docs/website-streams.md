# Website and direct media streams (beta.3)

The wall and picture-in-picture overlays can now use RTSP, direct HTTP/HTTPS
video, HLS, DASH (subject to LibVLC codec support), and public website sources
recognized by Streamlink or yt-dlp. Existing RTSP configurations keep working.

In **Streams → Edit**, paste the original website or media URL under **Source URL**.
Choose a **Source type**:

| Type | Behavior |
| --- | --- |
| Auto | Plays RTSP and recognizable direct media extensions directly. For other HTTP(S) URLs, tries Streamlink followed by yt-dlp. |
| Direct stream | Hands the URL directly to LibVLC; use this for extensionless HTTP media endpoints. |
| Streamlink | Uses Streamlink only, with an error if it cannot resolve the source. |
| yt-dlp | Uses yt-dlp only; useful for website videos and supported live channels. |

**Website quality limit** defaults to 720p. Choose a higher cap or Best available
if a source has no suitable lower-quality rendition. A cap limits the selected
website rendition; it does not resize direct streams or transcode video.

**Test stream** resolves website sources and checks that media bytes arrive.
Direct URLs receive syntax validation only; the result explicitly says this.
Use **Save & apply** to check actual decoding and framing in the wall.
The native Streams editor also provides source type and quality controls.

Overlays can use their own website URL or a linked main stream. Linked overlays
inherit source type and quality while retaining their own position and framing.
RTSP transport options apply only to RTSP playback. Website links are resolved
again during recovery, rather than saving an expiring media address.

## Packaging and limits

Streamlink, yt-dlp, its EJS scripts and the Deno JavaScript runtime ship inside the
beta package. Python and a separate external player are not required. Helper
versions update with the normal application installer. Each resolved website
source uses a background process and loopback relay; consider CPU, memory and
upstream connection limits when building a large wall.

This first integration targets **public, non-DRM, combined video/audio formats**.
Login-cookie import, site-specific authentication, separate-track FFmpeg muxing,
and independent helper updates are not included yet. Streamlink plugins needing
FFmpeg are not supported by the bundled package. Website changes, geographic
restrictions or bot checks can prevent playback even for a listed site.
Finite videos currently use the existing recovery behavior when they end.

Source URLs are retained in the local configuration. The existing credential-free
export removes query strings, so website URLs containing a video ID or other
required query parameter must be reentered after importing that sanitized export.
Full configuration transfer retains the original URLs. Resolved media URLs and
upstream authentication headers are never returned by Test stream.

Supported providers: [Streamlink plugins](https://streamlink.github.io/plugins.html)
and [yt-dlp supported sites](https://github.com/yt-dlp/yt-dlp/blob/master/supportedsites.md).
See [helper build instructions](../integrations/stream-resolver/README.md) for development.
