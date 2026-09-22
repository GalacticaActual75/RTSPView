"""Real frozen-executable extraction/relay/lifetime checks against a local fixture."""
import json
from pathlib import Path
from queue import Queue
import subprocess
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.request import urlopen

payload = b"local media fixture" * 200
class Origin(BaseHTTPRequestHandler):
    def log_message(self, *args): pass
    def do_GET(self):
        body = b'<html><head><title>Local fixture</title></head><body><video src="/sample.mp4" controls></video></body></html>' if self.path == "/watch" else payload
        self.send_response(200)
        self.send_header("Content-Type", "text/html" if self.path == "/watch" else "video/mp4")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

server = ThreadingHTTPServer(("127.0.0.1", 0), Origin)
threading.Thread(target=server.serve_forever, daemon=True).start()
executable = Path(__file__).resolve().parents[2] / "artifacts/stream-resolver/stream-resolver/stream-resolver.exe"
try:
    for mode in (0, 3):
        process = subprocess.Popen([str(executable)], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            # Hostnames exercise shared urllib3 normalization; numeric loopback
            # addresses bypass it and hid the provider import-order conflict.
            process.stdin.write(json.dumps({"url": f"http://localhost:{server.server_port}/watch", "mode": mode, "maximumHeight": 720}) + "\n")
            process.stdin.flush()
            line = Queue()
            threading.Thread(target=lambda: line.put(process.stdout.readline()), daemon=True).start()
            response = json.loads(line.get(timeout=30))
            assert "error" not in response, response
            assert response["provider"] == "yt-dlp", response
            with urlopen(response["url"], timeout=10) as media:
                assert media.read() == payload
            process.stdin.close()
            assert process.wait(timeout=8) == 0
        finally:
            if process.poll() is None: process.kill(); process.wait(timeout=5)
            process.stdout.close(); process.stderr.close()
    print("PASS packaged Auto fallback, yt-dlp HTML extraction, HTTP relay, quality request and parent-exit cleanup")
finally:
    server.shutdown(); server.server_close()
