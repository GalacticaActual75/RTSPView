import asyncio
import unittest
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch
from reader import Reader, contact


def child(value=None, **info):
    return SimpleNamespace(model="T110", alias="Door", device_id="sensor", sys_info=info,
                           features={} if value is None else {"is_open": SimpleNamespace(value=value)})


class ContactChecks(unittest.TestCase):
    def test_states(self):
        self.assertEqual(contact(child(True), "h")["state"], 2)
        self.assertEqual(contact(child(False), "h")["state"], 1)
        self.assertEqual(contact(child(), "h")["state"], 0)
        self.assertEqual(contact(child(open=True), "h")["state"], 2)
        self.assertEqual(contact(child(True, status="offline"), "h")["state"], 0)
        self.assertEqual(contact(child(False, online=False), "h")["state"], 0)

    def test_other_sensors_ignored(self):
        other = child(True)
        other.model = "T100"
        self.assertIsNone(contact(other, "h"))


class HubChecks(unittest.IsolatedAsyncioTestCase):
    async def test_network_discovery_without_credentials(self):
        devices = {"192.0.2.10": SimpleNamespace(model="H100", alias="Hall", disconnect=AsyncMock()),
                   "192.0.2.11": SimpleNamespace(model="H200", alias=None, disconnect=AsyncMock()),
                   "192.0.2.12": SimpleNamespace(model="P100", alias="Plug", disconnect=AsyncMock())}
        with patch("reader.Discover.discover", new=AsyncMock(return_value=devices)) as discover:
            result = await Reader().read({"discoverHubs": True})
            self.assertEqual([h["name"] for h in result["discoveredHubs"]], ["Hall", "H200"])
            self.assertEqual(len({h["id"] for h in result["discoveredHubs"]}), 2)
            self.assertEqual(result["sensors"], [])
            discover.assert_awaited_once_with(discovery_timeout=5, timeout=5)
            for device in devices.values():
                device.disconnect.assert_awaited_once()

    async def test_both_hubs_reuse_and_failure(self):
        first = SimpleNamespace(model="H100", children=[child(True)], update=AsyncMock(), disconnect=AsyncMock())
        second = SimpleNamespace(model="H200", children=[child(False)], update=AsyncMock(), disconnect=AsyncMock())
        request = {"username": "test@example.invalid", "password": "test-only",
                   "hubs": [{"id": "one", "name": "One", "host": "one.example"}, {"id": "two", "name": "Two", "host": "two.example"}]}
        with patch("reader.Discover.discover_single", new=AsyncMock(side_effect=[first, second])) as discover:
            reader = Reader()
            data = await reader.read(request)
            self.assertEqual([s["state"] for s in data["sensors"]], [2, 1])
            self.assertEqual([s["hubId"] for s in data["sensors"]], ["one", "two"])
            await reader.read(request)
            self.assertEqual(discover.await_count, 2)
            first.update.side_effect = TimeoutError("never-expose-account-or-password")
            data = await reader.read(request)
            self.assertFalse(data["hubs"][0]["connected"])
            self.assertEqual(len(data["sensors"]), 1)
            self.assertNotIn("never-expose", str(data))
            await reader.close()
            second.disconnect.assert_awaited()


if __name__ == "__main__":
    unittest.main()
