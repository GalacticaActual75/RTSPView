# Plugins

Plugins in the main navigation (between Automation and Settings), or Settings → Plugins, controls Weather, Aircraft, yt-dlp, ONVIF, Streamlink, Picture in picture, and Automations for this host. Apply changes persists all seven switches. Existing installations retain enabled behavior until a switch is turned off.

Disabling a plugin hides its controls and stops its runtime work. Saved locations, layouts, source selections, overlays, rules, and credentials are retained. Re-enabling restores access to the same configuration. Disabled weather and aircraft tiles leave their reserved layout area empty; camera-backed aircraft overlays return to the camera.

The Automations switch covers MQTT and Tapo sensor rules. Picture in picture is independent: disabling it suppresses overlay playback while other enabled automation actions remain available. Streamlink and yt-dlp source selection is independent; Auto uses only enabled resolvers. Direct RTSP and direct media streams continue to work.

The switches travel with configuration exports and imports. Native Live View and the background services apply saved changes automatically; other browser sessions check for switch changes every five seconds.

Each switch includes an explanation. Both entry points share the same form, including unapplied changes.
