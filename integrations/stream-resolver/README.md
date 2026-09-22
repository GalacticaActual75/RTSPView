# Website stream helper

Build with Python 3.12 on Windows x64: install `requirements.txt`, run
`test_resolver.py`, then `build.py`. The applications copy the complete frozen
package to their Streaming directory. No system Python installation is needed.
The build pins all Python dependencies and checks the bundled Deno archive hash.
Dependencies, Deno license output and our source inputs accompany the helper.

Auto bypasses resolution for RTSP and recognizable direct media extensions.
Extensionless direct URLs require Direct mode. Website Auto tries Streamlink,
then yt-dlp. Website quality is capped at 720p by default; Best removes the cap.
Direct media keeps its supplied quality. A failed/expired website source is
resolved again on each viewer recovery attempt. Resolution has a 60-second
deadline, at most three concurrent resolutions per application, and a cancellable
queue. Stopping a tile closes the relay and terminates the process tree; closing
the parent's stdin pipe also terminates the helper after a host crash.

The relay binds only to loopback with a random per-session path. Website URLs
travel through stdin, never command arguments. Library logs are suppressed to
avoid leaking signed media URLs. yt-dlp headers and scoped extraction cookies
remain within the helper. HTTP Range requests are forwarded for file playback.

This beta supports public, non-DRM sources with combined video/audio formats.
Separate-track muxing, user-supplied login cookies, per-site authentication and
independent in-app helper updates are follow-up work. Some Streamlink plugins
require FFmpeg; it is not bundled in this initial implementation. YouTube's JS
runtime (Deno) and yt-dlp EJS scripts are bundled; site restrictions can still
prevent playback. Helpers update with the normal beta installer.

Test stream checks website resolution and receipt of media bytes; actual codec
decoding is verified by Save & apply in the wall. Direct-source testing validates
the URL only and says so. No media files are downloaded to disk.
