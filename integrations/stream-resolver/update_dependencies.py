"""Select published upstream dependencies; retain exact installed inputs in the bundle."""
import base64
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import urllib.request
from packaging.version import Version

def latest(name, prereleases=False):
    with urllib.request.urlopen(f"https://pypi.org/pypi/{name}/json", timeout=30) as response:
        releases = json.load(response)["releases"]
    return str(max(Version(v) for v, files in releases.items()
                   if files and any(not f.get("yanked", False) for f in files)
                   and (prereleases or not Version(v).is_prerelease)))

versions = {"yt-dlp": latest("yt-dlp", True), "streamlink": latest("streamlink"), "yt-dlp-ejs": latest("yt-dlp-ejs")}
source_revision = hashlib.sha256(b"".join(Path(__file__).with_name(name).read_bytes()
    for name in ("resolver.py", "build.py"))).hexdigest()
try:
    with urllib.request.urlopen("https://github.com/GalacticaActual75/RTSPView/releases/download/streaming-current/manifest.json", timeout=30) as response:
        current = json.loads(base64.b64decode(json.load(response)["payload"]))
    unchanged = (current["YtDlp"] == versions["yt-dlp"] and current["Streamlink"] == versions["streamlink"]
                 and current.get("Ejs") == versions["yt-dlp-ejs"] and current.get("SourceRevision") == source_revision)
except Exception:
    unchanged = False
if unchanged and "--force" not in sys.argv:
    print("Upstream dependencies unchanged.")
    sys.exit(0)
subprocess.run([sys.executable, "-m", "pip", "install", "--upgrade", *[f"{k}=={v}" for k, v in versions.items()]], check=True)
freeze = subprocess.check_output([sys.executable, "-m", "pip", "freeze"], text=True)
Path(__file__).with_name("requirements.txt").write_text(freeze, encoding="utf-8")
Path("artifacts/streaming-update-needed").write_text(json.dumps(versions), encoding="utf-8")
