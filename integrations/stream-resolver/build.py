"""Build the pinned Windows helper, runtime, source inputs and dependency notices."""
import hashlib
import importlib.metadata
from pathlib import Path
import shutil
import subprocess
import sys
import urllib.request
import zipfile

here = Path(__file__).resolve().parent
output = here.parents[1] / "artifacts" / "stream-resolver"
subprocess.run([sys.executable, "-m", "PyInstaller", "--noconfirm", "--clean", "--onedir",
                "--name", "stream-resolver", "--distpath", str(output), "--workpath", str(output / "work"),
                "--specpath", str(output), "--collect-all", "streamlink", "--collect-all", "yt_dlp",
                "--collect-all", "yt_dlp_ejs", str(here / "resolver.py")], check=True)
package = output / "stream-resolver"
source = package / "source"
source.mkdir(exist_ok=True)
for path in here.iterdir():
    if path.is_file(): shutil.copy2(path, source / path.name)
notices = package / "licenses"
notices.mkdir(exist_ok=True)
shutil.copy2(Path(sys.base_prefix) / "LICENSE.txt", notices / "Python-LICENSE.txt")
for dist in importlib.metadata.distributions():
    for file in dist.files or []:
        if ".dist-info/" in str(file).replace("\\", "/") and any(word in file.name.lower() for word in ("license", "copying", "notice")):
            target = notices / dist.metadata["Name"] / file.name
            target.parent.mkdir(exist_ok=True)
            shutil.copy2(dist.locate_file(file), target)

version = "v2.9.7"
archive = output / "deno.zip"
digest = "a0c3101b4158d1dfb7d6a78a7bf0f3de80c96bb423c152beec8beb22786f2238"
if not archive.exists():
    urllib.request.urlretrieve(f"https://github.com/denoland/deno/releases/download/{version}/deno-x86_64-pc-windows-msvc.zip", archive)
if hashlib.sha256(archive.read_bytes()).hexdigest() != digest:
    raise ValueError("Deno archive checksum mismatch")
with zipfile.ZipFile(archive) as bundle:
    (package / "deno.exe").write_bytes(bundle.read("deno.exe"))
subprocess.run([str(package / "deno.exe"), "--version"], check=True)
urllib.request.urlretrieve(f"https://raw.githubusercontent.com/denoland/deno/{version}/LICENSE.md", notices / "Deno-LICENSE.txt")
subprocess.run([str(package / "stream-resolver.exe"), "--version"], check=True)
print(f"Built {package}")
