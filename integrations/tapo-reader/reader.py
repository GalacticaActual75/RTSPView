"""Local, read-only Tapo contact-sensor reader. GPL-3.0-or-later.

Each stdin line is {username,password,hubs:[{id,name,host}]}.
Each stdout line is {hubs:[...],sensors:[...]}; no credentials or raw errors.
The protocol contains device data only, with no display/RTSPView commands.
"""
import asyncio
import json
import logging
import sys

from kasa import Discover
from kasa.exceptions import AuthenticationError

logging.disable(logging.CRITICAL)


def contact(child, hub_id):
    if not str(child.model).upper().startswith("T110"):
        return None
    info = child.sys_info
    offline = info.get("status") == "offline" or info.get("online") in (False, 0)
    feature = child.features.get("is_open")
    value = (feature.value if feature is not None else info.get("open")) if not offline else None
    state = 2 if value is True or value == 1 else 1 if value is False or value == 0 else 0
    return {"hubId": hub_id, "deviceId": str(child.device_id)[:256],
            "name": str(child.alias or "Door sensor")[:100], "model": str(child.model)[:40], "state": state}


class Reader:
    def __init__(self):
        self.devices = {}

    async def close(self):
        for device in self.devices.values():
            try:
                await device.disconnect()
            except Exception:
                pass
        self.devices.clear()

    async def read(self, request):
        username, password = request["username"], request["password"]
        hubs = request["hubs"]
        if len(hubs) > 8:
            raise ValueError("Too many hubs")
        desired = {(h["host"], username, password) for h in hubs}
        for key in list(self.devices):
            if key not in desired:
                device = self.devices.pop(key)
                await device.disconnect()

        async def hub_read(hub):
            key = (hub["host"], username, password)
            status = {"id": hub["id"], "name": hub["name"][:100], "model": "", "connected": False, "message": "Hub unavailable"}
            try:
                async with asyncio.timeout(15):
                    device = self.devices.get(key)
                    if device is None:
                        device = await Discover.discover_single(hub["host"], username=username, password=password, timeout=5)
                        if device is None:
                            return status, []
                        self.devices[key] = device
                    await device.update(update_children=False)
                    status["model"] = str(device.model)[:40]
                    if not status["model"].upper().startswith(("H100", "H200")):
                        status["message"] = "This address is not an H100 or H200 hub."
                        return status, []
                    sensors = []
                    for child in device.children[:64]:
                        item = contact(child, hub["id"])
                        if item:
                            sensors.append(item)
                    status.update(connected=True, message=f"Connected · {len(sensors)} T110 sensor(s)")
                    return status, sensors
            except AuthenticationError:
                status["message"] = "Authentication failed. Check Tapo credentials and Third-Party Compatibility."
            except Exception:
                status["message"] = "Hub unavailable. Check the address, network and Third-Party Compatibility."
            device = self.devices.pop(key, None)
            if device is not None:
                try:
                    await device.disconnect()
                except Exception:
                    pass
            return status, []

        results = await asyncio.gather(*(hub_read(h) for h in hubs))
        return {"hubs": [r[0] for r in results], "sensors": [s for r in results for s in r[1]]}


async def main():
    reader = Reader()
    try:
        while True:
            line = await asyncio.to_thread(sys.stdin.readline, 65537)
            if not line:
                break
            try:
                if len(line) > 65536:
                    break
                result = await reader.read(json.loads(line))
            except Exception:
                result = {"hubs": [], "sensors": []}
            print(json.dumps(result, ensure_ascii=True), flush=True)
    finally:
        await reader.close()


if __name__ == "__main__":
    asyncio.run(main())
