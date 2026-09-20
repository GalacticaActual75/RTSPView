# Tapo contact-sensor reader

This independently runnable, read-only GPL-3.0-or-later helper uses
[python-kasa 0.10.2](https://github.com/python-kasa/python-kasa) to query T110
contact sensors through H100/H200 hubs on the local network. It has no UI,
network listener or camera-wall actions. RTSPView uses its JSON standard-input /
standard-output protocol. The Windows package includes Python; users do not need
to install it. Credentials never appear in command-line arguments or output.

One JSON request per input line:

```json
{"username":"user@example.invalid","password":"example-only","hubs":[{"id":"hub-id","name":"Hall hub","host":"hub.example"}]}
```

For a local network hub scan, send `{"discoverHubs":true}`. No credentials are needed. The response adds `discoveredHubs`, containing only H100/H200 candidates with generated IDs, names and IP addresses. Broadcast discovery may not cross subnets; manual addresses remain supported. Discovery connections are closed after each scan. A missing `discoveredHubs` field indicates a failed scan, distinct from an empty successful result.

The response contains hub connection results and T110 sensor IDs/names/states.
States: 0 = unavailable, 1 = closed, 2 = open. Connections are reused until their
address or account changes. Each request refreshes the hub inventory. A failed
hub produces no current sensor readings; consumers must mark missing readings
unavailable. A hub may keep a battery sensor's last reported value; this does not
prove the battery sensor is currently reachable. Short door changes between
polls may not be observed.

Build on Windows x64 using Python 3.12:

```powershell
python -m venv .toolchain/tapo-venv
.toolchain/tapo-venv/Scripts/python.exe -m pip install -r integrations/tapo-reader/requirements.txt
.toolchain/tapo-venv/Scripts/python.exe integrations/tapo-reader/build.py
```

The result is `artifacts/tapo-reader/tapo-reader/`, ready to copy into the
Controller's `Tapo` directory. Build scripts, helper source, exact dependency
source distributions and license notices are included in the package. The
helper is licensed separately from the surrounding application; see LICENSE.
