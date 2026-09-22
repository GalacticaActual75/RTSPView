"""Generate synthetic video and verify real LibVLC decoding without showing windows.

Run with the streaming venv after pip install -r integrations/stream-resolver/requirements-test.txt.
Pass the .NET SDK executable as the first argument if it is not on PATH.
"""
import functools
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import threading
import time
import re
from onvif_playback_fixture import start_camera, soap_response

import imageio_ffmpeg

root = Path(__file__).resolve().parents[1]
dotnet = sys.argv[1] if len(sys.argv) > 1 else "dotnet"
with tempfile.TemporaryDirectory(prefix="RTSPView-streaming-") as directory:
    path = Path(directory)
    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    hidden = {"creationflags": subprocess.CREATE_NO_WINDOW} if os.name == "nt" else {}
    subprocess.run([ffmpeg, "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=15", "-f", "lavfi", "-i", "sine=frequency=440", "-t", "12", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-g", "15", "-c:a", "aac", "-movflags", "+faststart", str(path / "sample.mp4")], check=True, **hidden)
    subprocess.run([ffmpeg, "-loglevel", "error", "-i", str(path / "sample.mp4"), "-c", "copy", "-f", "hls", "-hls_time", "2", "-hls_list_size", "0", str(path / "stream.m3u8")], check=True, **hidden)
    subprocess.run([ffmpeg, "-loglevel", "error", "-i", str(path / "sample.mp4"), "-an", "-c:v", "libx264", "-x264-params", "aud=1:repeat-headers=1", "-f", "h264", str(path / "sample.h264")], check=True, **hidden)
    camera = start_camera((path / "sample.h264").read_bytes())
    for name, media in (("watch", "sample.mp4"), ("watch-hls", "stream.m3u8")):
        (path / name).write_text(f'<html><head><title>Local synthetic video</title></head><body><video src="/{media}"></video></body></html>')
    class Handler(SimpleHTTPRequestHandler):
        def log_message(self, *args): pass
        def do_POST(self):
            self.rfile.read(int(self.headers.get('Content-Length', '0')))
            operation = re.search(r'action="[^\"]*/([^/\"]+)"', self.headers.get('Content-Type', ''))
            body = soap_response(operation[1] if operation else '', f'http://127.0.0.1:{self.server.server_port}', camera.server_address[1])
            self.send_response(200); self.send_header('Content-Type', 'application/soap+xml'); self.send_header('Content-Length', str(len(body))); self.end_headers(); self.wfile.write(body)
        def guess_type(self, file):
            return "text/html" if Path(file).name in ("watch", "watch-hls") else super().guess_type(file)
        def do_GET(self):
            if self.path == "/hang": time.sleep(10); return
            try: super().do_GET()
            except (ConnectionError, BrokenPipeError): pass
    server = ThreadingHTTPServer(("127.0.0.1", 0), functools.partial(Handler, directory=directory))
    server.daemon_threads = True
    threading.Thread(target=server.serve_forever, daemon=True).start()
    try:
        environment = {**os.environ, "RTSPVIEW_STREAM_FIXTURE": f"http://127.0.0.1:{server.server_port}"}
        result = subprocess.run([dotnet, "run", "--project", "tests/RTSPView.StreamingChecks", "-c", "Release"], cwd=root, env=environment, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, timeout=180, **hidden)
        print(result.stdout, flush=True)
        result.check_returncode()
    finally:
        server.shutdown(); server.server_close()
        camera.shutdown(); camera.server_close()
