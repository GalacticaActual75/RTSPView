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
    from streamlink import Streamlink
    from streamlink.stream.hls import HLSStream
    from streamlink.stream.http import HTTPStream
    from yt_dlp import YoutubeDL

    url = request.get("url", "")
    mode = request.get("mode", 0)
    height = request.get("maximumHeight", 720)
    if urlsplit(url).scheme not in ("http", "https") or not urlsplit(url).hostname:
        raise SourceError("Website sources require an HTTP or HTTPS URL.")
    if mode not in (0, 2, 3) or height not in (0, 360, 480, 720, 1080, 1440, 2160):
        raise SourceError("Invalid source mode or quality limit.")
    session = Streamlink()
    session.set_option("http-timeout", 15)
    session.set_option("stream-timeout", 20)
    session.set_option("hls-playlist-reload-attempts", 3)
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
        server, url = make_server(stream)
        print(json.dumps({"url": url, "provider": provider}), flush=True)
        server.serve_forever()
    except SourceError as error:
        print(json.dumps({"error": str(error)}), flush=True)
    except Exception:
        print(json.dumps({"error": "Streaming helper could not start. Check the beta installation."}), flush=True)


if __name__ == "__main__":
    main()
