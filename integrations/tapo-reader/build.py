"""Build the standalone reader and include its reproducible source inputs."""
import hashlib
import importlib.metadata
import json
from pathlib import Path
import shutil
import subprocess
import sys
import urllib.request

here = Path(__file__).resolve().parent
root = here.parents[1]
output = root / "artifacts" / "tapo-reader"
subprocess.run([sys.executable, "-m", "PyInstaller", "--noconfirm", "--clean", "--onedir",
                "--name", "tapo-reader", "--distpath", str(output), "--workpath", str(output / "work"),
                "--specpath", str(output), "--collect-all", "kasa", "--collect-all", "mashumaro",
                "--copy-metadata", "python-kasa", str(here / "reader.py")], check=True)
package = output / "tapo-reader"
source = package / "source"
source.mkdir(exist_ok=True)
for name in ("reader.py", "build.py", "requirements.txt", "README.md", "LICENSE", "test_reader.py", "check_package.py"):
    shutil.copy2(here / name, source / name)
shutil.copy2(here / "LICENSE", package / "LICENSE-tapo-reader.txt")
notices = package / "licenses"
notices.mkdir(exist_ok=True)
shutil.copy2(Path(sys.base_prefix) / "LICENSE.txt", notices / "Python-LICENSE.txt")
manifest = []
# Source archives include build tools as well as runtime dependencies. Verify
# every archive using the digest published by its package index.
for requirement in (here / "requirements.txt").read_text().splitlines():
    if not requirement or requirement.startswith("#"):
        continue
    name, version = requirement.split("==")
    with urllib.request.urlopen(f"https://pypi.org/pypi/{name}/{version}/json", timeout=60) as response:
        metadata = json.load(response)
    archive = next(item for item in metadata["urls"] if item["packagetype"] == "sdist")
    destination = source / archive["filename"]
    if not destination.exists():
        with urllib.request.urlopen(archive["url"], timeout=120) as response:
            destination.write_bytes(response.read())
    if hashlib.sha256(destination.read_bytes()).hexdigest() != archive["digests"]["sha256"]:
        raise ValueError(f"Source digest mismatch: {name}")
    manifest.append({"name": name, "version": version, "file": destination.name, "sha256": archive["digests"]["sha256"]})
    dist = importlib.metadata.distribution(name)
    for file in dist.files or []:
        if ".dist-info/" in str(file).replace("\\", "/") and any(word in file.name.lower() for word in ("license", "copying", "notice")):
            target = notices / name / file.name
            target.parent.mkdir(exist_ok=True)
            shutil.copy2(dist.locate_file(file), target)
(source / "manifest.json").write_text(json.dumps(manifest, indent=2))
print(f"Built {package}")
