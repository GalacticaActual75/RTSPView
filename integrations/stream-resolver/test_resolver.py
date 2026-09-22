import io
import json
from pathlib import Path
import subprocess
import sys
import threading
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.request import Request, urlopen
from urllib.error import HTTPError
from unittest.mock import patch

import resolver


class ResolverChecks(unittest.TestCase):
    def test_media_preflight_preserves_bytes_and_reports_refusal(self):
        class Stream:
            def open(self): return io.BytesIO(b"video" * 100)
        prepared = resolver.prepare_stream(Stream())
        with prepared.open() as reader:
            self.assertEqual(reader.read(188) + reader.read(65536), b"video" * 100)
        class Blocked:
            session = type("Session", (), {"rtspview_http_status": 403})()
            def open(self): raise OSError("https://private.example/secret")
        with self.assertRaises(resolver.SourceError) as error: resolver.prepare_stream(Blocked())
        self.assertIn("HTTP 403", str(error.exception))
        self.assertNotIn("private", str(error.exception))

    def test_provider_imports_preserve_hostname_requests(self):
        # A fresh process uses the real production import order. Loopback IP
        # fixtures bypass urllib3 hostname normalization and missed this crash.
        code = """
import resolver
try:
    resolver.resolve({'url': 'invalid'})
except resolver.SourceError:
    pass
from requests import Request
from streamlink import Streamlink
url = Streamlink().http.prepare_request(Request('GET', 'https://media.example/video%2fpart')).url
assert url == 'https://media.example/video%2fpart', url
"""
        result = subprocess.run([sys.executable, '-c', code], cwd=Path(__file__).parent,
                                capture_output=True, text=True, timeout=15)
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_quality(self):
        streams = {"360p": "low", "720p60": "mid", "1080p": "high", "best": "high"}
        self.assertEqual(resolver.choose_stream(streams, 720), "mid")
        self.assertEqual(resolver.choose_stream(streams, 0), "high")
        with self.assertRaises(resolver.SourceError): resolver.choose_stream({"1080p": "high"}, 720)
        with self.assertRaises(resolver.SourceError): resolver.choose_stream({}, 720)

    def test_fallback_and_headers(self):
        from streamlink import Streamlink
        from yt_dlp import YoutubeDL
        media = {"url": "https://media.example/video.mp4", "protocol": "https", "http_headers": {"Referer": "https://source.example/", "Authorization": "private"}}
        with patch.object(Streamlink, "streams", side_effect=RuntimeError("private upstream URL")), patch.object(YoutubeDL, "extract_info", return_value=media):
            stream, provider = resolver.resolve({"url": "https://source.example/watch", "mode": 0})
            self.assertEqual(provider, "yt-dlp")
            self.assertEqual(stream.args["headers"], media["http_headers"])
            with self.assertRaises(resolver.SourceError) as error: resolver.resolve({"url": "https://source.example/watch", "mode": 2})
            self.assertNotIn("private", str(error.exception))

    def test_http_relay_range_headers_and_token(self):
        from streamlink import Streamlink
        from streamlink.stream.http import HTTPStream
        observed = []
        class Origin(BaseHTTPRequestHandler):
            def log_message(self, *args): pass
            def do_GET(self):
                observed.append((self.headers.get("Range"), self.headers.get("Authorization")))
                self.send_response(206)
                self.send_header("Content-Length", "4")
                self.send_header("Content-Range", "bytes 2-5/8")
                self.end_headers()
                self.wfile.write(b"test")
        origin = ThreadingHTTPServer(("127.0.0.1", 0), Origin)
        threading.Thread(target=origin.serve_forever, daemon=True).start()
        stream = HTTPStream(Streamlink(), f"http://127.0.0.1:{origin.server_port}/file", headers={"Authorization": "private"})
        server, url = resolver.make_server(stream)
        threading.Thread(target=server.serve_forever, daemon=True).start()
        try:
            with urlopen(Request(url, headers={"Range": "bytes=2-5"}), timeout=5) as response:
                self.assertEqual(response.read(), b"test")
                self.assertEqual(response.status, 206)
            self.assertEqual(observed, [("bytes=2-5", "private")])
            with self.assertRaises(HTTPError) as error: urlopen(url + "wrong", timeout=5)
            self.assertEqual(error.exception.code, 404)
        finally:
            server.shutdown(); server.server_close()
            origin.shutdown(); origin.server_close()

    def test_stream_relay(self):
        class Stream:
            def open(self): return io.BytesIO(b"video" * 100)
        server, url = resolver.make_server(Stream())
        threading.Thread(target=server.serve_forever, daemon=True).start()
        try:
            with urlopen(url, timeout=5) as response: self.assertEqual(response.read(), b"video" * 100)
        finally:
            server.shutdown(); server.server_close()

    def test_parent_pipe_closure(self):
        process = subprocess.Popen([sys.executable, str(Path(__file__).with_name("resolver.py"))], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        process.stdin.write(json.dumps({"url": "https://example.invalid/watch"}) + "\n")
        process.stdin.flush()
        process.stdin.close()
        try: self.assertEqual(process.wait(timeout=8), 0)
        finally:
            if process.poll() is None: process.kill()
            process.stdout.close(); process.stderr.close()


if __name__ == "__main__": unittest.main()
