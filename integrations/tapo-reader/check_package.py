"""Exercise the frozen executable without any live hubs or account credentials."""
import json
from pathlib import Path
import subprocess

root = Path(__file__).resolve().parents[2]
binary = root / "artifacts/tapo-reader/tapo-reader/tapo-reader.exe"
request = {"username": "package-test@example.invalid", "password": "package-test-only", "hubs": []}
result = subprocess.run([str(binary)], input=json.dumps(request) + "\n" + json.dumps(request) + "\n",
                        capture_output=True, text=True, timeout=30)
assert result.returncode == 0, result.stderr
assert [json.loads(line) for line in result.stdout.splitlines()] == [{"hubs": [], "sensors": []}] * 2
assert request["password"] not in result.stdout + result.stderr
assert (binary.parent / "source/manifest.json").exists()
assert (binary.parent / "LICENSE-tapo-reader.txt").exists()
print("PASS packaged reader: startup, repeated JSON requests, clean EOF, no secret output, source and license bundle")
