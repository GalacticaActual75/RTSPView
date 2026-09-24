"""Resolve a website and relay its media to LibVLC; one process per active source.

The first stdin line is configuration. Remaining stdin is a lifetime lease:
parent exit/pipe closure terminates the helper even if a network read is stuck.
Only structured, credential-free status is written to stdout.
"""
import json
import logging
import os
import re
import secrets
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlsplit


class SourceError(Exception):
    pass


def choose_stream(streams, maximum_height):
    if not streams:
        raise SourceError("Source is offline or has no playable streams.")
    if not maximum_height:
        if "best" in streams:
            return streams["best"]
    ranked = []
    for name, stream in streams.items():
        match = re.match(r"^(\d+)p(?:\d+)?(?:_|$)", name)
        if match and (not maximum_height or int(match[1]) <= maximum_height):
            ranked.append((int(match[1]), name, stream))
    if ranked:
        return max(ranked, key=lambda item: item[:2])[2]
    if maximum_height:
        raise SourceError("No stream is available within the selected quality limit. Try a higher limit or Best.")
    return next(iter(streams.values()))


class QuietLogger:
    def debug(self, *args, **kwargs): pass
    def warning(self, *args, **kwargs): pass
    def error(self, *args, **kwargs): pass


def resolve(request):
    # Both libraries wrap urllib3's URL-normalization regex. yt-dlp's wrapper
    # cannot forward attributes through Streamlink's wrapper, so load it first.
    from yt_dlp import YoutubeDL
    from streamlink import Streamlink
    from streamlink.stream.hls import HLSStream
    from streamlink.stream.http import HTTPStream

    url = request.get("url", "")
    mode = request.get("mode", 0)
    height = request.get("maximumHeight", 720)
    if urlsplit(url).scheme not in ("http", "https") or not urlsplit(url).hostname:
        raise SourceError("Website sources require an HTTP or HTTPS URL.")
    if mode not in (0, 2, 3) or height not in (0, 360, 480, 720, 1080, 1440, 2160):
        raise SourceError("Invalid source mode or quality limit.")
    session = Streamlink()
    session.rtspview_http_status = None
    def remember_response(response, **kwargs):
        if response.status_code >= 400:
            session.rtspview_http_status = response.status_code
    session.http.hooks.setdefault("response", []).append(remember_response)
    session.set_option("http-timeout", 15)
    session.set_option("stream-timeout", 20)
    session.set_option("hls-playlist-reload-attempts", 3)
    session.set_option("hls-segment-attempts", 1)
    if mode in (0, 2):
        try:
            streams = session.streams(url)
            return choose_stream(streams, height), "Streamlink"
        except Exception as error:
            if mode == 2:
                if isinstance(error, SourceError): raise
                raise SourceError("Streamlink could not open this source. Check the URL, login requirements or try yt-dlp.") from None
    # Use a single video+audio format; split tracks require a later muxing stage.
    quality = f"[height<=?{height}]" if height else ""
    options = {
        "quiet": True, "no_warnings": True, "logger": QuietLogger(),
        "noplaylist": True, "skip_download": True, "socket_timeout": 15,
        "retries": 1, "extractor_retries": 1,
        "format": f"best{quality}[protocol^=http]/best{quality}[protocol^=m3u8]",
        "cachedir": False,
    }
    # A packaged JS runtime is optional; never download executable code at runtime.
    deno = os.path.join(os.path.dirname(sys.executable), "deno.exe")
    if os.path.isfile(deno):
        options["js_runtimes"] = {"deno": {"path": deno}}
    try:
        with YoutubeDL(options) as downloader:
            info = downloader.extract_info(url, download=False)
            if not info or info.get("_type") in ("playlist", "multi_video") or not info.get("url"):
                raise SourceError("Choose a single video or live channel URL, not a playlist.")
            if info.get("has_drm"):
                raise SourceError("This source requires DRM playback and cannot be opened here.")
            session.http.cookies.update(downloader.cookiejar)
            protocol = info.get("protocol", "")
            stream_type = HLSStream if protocol.startswith("m3u8") else HTTPStream
            if protocol not in ("http", "https", "m3u8", "m3u8_native"):
                raise SourceError("This source needs a media format not supported by the beta resolver.")
            return stream_type(session, info["url"], headers=info.get("http_headers", {})), "yt-dlp"
    except SourceError:
        raise
    except Exception:
        raise SourceError("yt-dlp could not open this source. It may be offline, require login, or lack a combined video/audio format at this quality.") from None


class PrefetchedStream:
    """Preserve the bytes used to verify HLS playback before announcing readiness."""
    def __init__(self, stream, reader, first):
        self.stream, self.reader, self.first = stream, reader, first

    def open(self):
        if self.reader is None:
            return self.stream.open()
        reader, first = self.reader, self.first
        self.reader = None
        class Reader:
            def __init__(self): self.pending = first
            def read(self, size):
                if self.pending:
                    data, self.pending = self.pending[:size], self.pending[size:]
                    return data
                return reader.read(size)
            def __enter__(self): return self
            def __exit__(self, *args): reader.close()
        return Reader()


def prepare_stream(stream):
    from streamlink.stream.http import HTTPStream
    reader = None
    try:
        reader = stream.open()
        first = reader.read(188)
        if not first: raise SourceError("Empty media response")
        if type(stream) is HTTPStream:
            reader.close()  # Keep HTTP Range/seek handling on subsequent requests.
            return stream
        return PrefetchedStream(stream, reader, first)
    except Exception:
        if reader is not None: reader.close()
        status = getattr(getattr(stream, "session", None), "rtspview_http_status", None)
        if status in (401, 403):
            raise SourceError(f"The website refused access to the video (HTTP {status}). Resolving the page succeeded, but playback is unavailable through this provider.") from None
        raise SourceError("The website resolved, but no video data arrived. The source may be offline or refusing playback through this provider.") from None


def make_server(stream):
    token = "/" + secrets.token_urlsafe(32)
    lock = threading.Lock()

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *args): pass

        def do_GET(self):
            if self.path != token:
                self.send_error(404)
                return
            if not lock.acquire(blocking=False):
                self.send_error(409)
                return
            try:
                # HTTP files need Range forwarding for MP4 metadata and seeking.
                from streamlink.stream.http import HTTPStream
                if type(stream) is HTTPStream:
                    args = dict(stream.args)
                    headers = dict(args.pop("headers", {}))
                    if self.headers.get("Range"):
                        headers["Range"] = self.headers["Range"]
                    with stream.session.http.get(stream=True, headers=headers, **args) as response:
                        self.send_response(response.status_code)
                        for key in ("Content-Type", "Content-Length", "Content-Range", "Accept-Ranges"):
                            if key in response.headers: self.send_header(key, response.headers[key])
                        self.end_headers()
                        for chunk in response.iter_content(65536): self.wfile.write(chunk)
                else:
                    with stream.open() as reader:
                        first = reader.read(188)
                        if not first: raise SourceError("Empty stream")
                        self.send_response(200)
                        self.send_header("Content-Type", "application/octet-stream")
                        self.end_headers()
                        self.wfile.write(first)
                        while chunk := reader.read(65536): self.wfile.write(chunk)
            except Exception:
                # Never log upstream URLs, cookies or library exception text.
                self.close_connection = True
            finally:
                lock.release()

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    server.daemon_threads = True
    return server, f"http://127.0.0.1:{server.server_port}{token}"


def main():
    logging.disable(logging.CRITICAL)
    if "--self-test" in sys.argv:
        # Exercise both imports in the required order and a real localhost relay.
        from yt_dlp import YoutubeDL
        from streamlink import Streamlink
        from streamlink.stream.http import HTTPStream
        from importlib.metadata import version
        from pathlib import Path
        import subprocess
        import urllib.request
        class Fixture(BaseHTTPRequestHandler):
            def log_message(self, *args): pass
            def do_GET(self):
                self.send_response(200)
                self.end_headers()
                self.wfile.write(b"rtspview-streaming-health")
        source = ThreadingHTTPServer(("127.0.0.1", 0), Fixture)
        threading.Thread(target=source.serve_forever, daemon=True).start()
        session = Streamlink()
        stream = HTTPStream(session, f"http://localhost:{source.server_port}/media")
        relay, address = make_server(prepare_stream(stream))
        threading.Thread(target=relay.serve_forever, daemon=True).start()
        with urllib.request.urlopen(address, timeout=3) as response:
            assert response.read() == b"rtspview-streaming-health"
        if getattr(sys, "frozen", False):
            subprocess.run([str(Path(sys.executable).parent / "deno.exe"), "--version"],
                           check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=5)
        print(json.dumps({"protocol": 1, **{name: version(name) for name in ("streamlink", "yt-dlp")}}))
        relay.shutdown()
        source.shutdown()
        return
    if "--version" in sys.argv:
        from importlib.metadata import version
        print(json.dumps({name: version(name) for name in ("streamlink", "yt-dlp")}))
        return
    request = json.loads(sys.stdin.readline(65536))
    def watch_parent():
        sys.stdin.read()
        os._exit(0)
    threading.Thread(target=watch_parent, daemon=True).start()
    try:
        stream, provider = resolve(request)
        stream = prepare_stream(stream)
        server, url = make_server(stream)
        print(json.dumps({"url": url, "provider": provider}), flush=True)
        server.serve_forever()
    except SourceError as error:
        print(json.dumps({"error": str(error)}), flush=True)
    except Exception:
        print(json.dumps({"error": "Streaming helper could not start. Check the beta installation."}), flush=True)


if __name__ == "__main__":
    main()
