# Admin panel previews

These screenshots show the RTSPView 1.0.46 web administration interface using synthetic data. Camera images are illustrations marked **SYNTHETIC IMAGE**, not camera footage. Camera names, source addresses, weather, sensor rules and status readings are examples. They do not demonstrate physical-device connectivity or performance.

## Monitor

![Monitor](monitor.png)

## Streams

![Streams](streams.png)

## Stream editor

![Stream editor](stream-editor.png)

## ONVIF profiles

![ONVIF profiles](onvif.png)

## Layouts

![Layouts](layouts.png)

## Picture in picture

![Picture in picture](picture-in-picture.png)

## MQTT automation

![MQTT automation](mqtt.png)

## Tapo automation

![Tapo automation](tapo.png)

## Weather Widget

![Weather Widget](weather.png)

## Reproducing the preview

From the repository root, run:

```powershell
$env:PREVIEW_PORT = '5200'
node tests/ui-preview.cjs --documentation
```

Open `http://127.0.0.1:5200` in a browser. The loopback-only test server serves the application's web assets with fabricated API responses from `tests/documentation-demo.cjs`. It does not load installed user configuration, connect to cameras or brokers, or start the Windows Viewer. Its content security policy limits automatic page requests to the local preview. Close the server with Ctrl+C when finished.

The screenshots are direct browser captures. Before replacing them, visually review every image for private information and use only this synthetic fixture.
