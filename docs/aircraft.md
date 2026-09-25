# Aircraft widgets and tiles

Aircraft views show airborne traffic around a configured location using ADSB.lol. They share the weather widget's dark/light surface, opacity, corners, padding, accent, font and icon controls.

## Add to a camera

In **Layouts**, select a camera tile and choose **Aircraft Widget**. Search for a city, enter coordinates, or choose **Use weather location**. Set the radius and optional altitude limits, then choose the position and size. **Save & apply** saves the widget for that camera across standard and automation layouts. The camera keeps playing behind it. Weather and aircraft widgets have independent positions; aircraft defaults to bottom right, weather to bottom left.

**Hide when no aircraft are nearby** hides a successfully updated empty overlay. An unavailable or failed feed remains visibly labelled rather than looking like a clear sky. Uncheck **Enable Aircraft Widget** to remove it from the camera.

## Replace a tile

Use **Add aircraft**. A full grid offers a camera picker and **Replace tile with aircraft**. A selected camera also has **Replace with aircraft**. Choose:

- **Featured flight**: one nearby aircraft, held for 20 seconds while still eligible before selecting the nearest again.
- **Flight board**: nearest aircraft first, up to five flights, with fewer rows/details when the tile is small.

**Use in layout draft** changes the draft; **Apply to wall** saves and displays it. Aircraft tiles can be moved, resized and removed like weather tiles. They are available in standard layouts; camera-attached widgets also appear when automation selects their host camera.

Fields include callsign (falling back to registration or transponder address), type/registration when available, altitude, ground speed, distance and bearing from the location, ground track, and climb/descent rate. Distance is horizontal distance. The plane icon does not identify a plane's position in the camera image. Routes, airline names, logos and photos are not supplied in this version.

The radius input uses statute miles and altitude filters use feet. Display units can be feet/knots/miles or meters/km/h/kilometers. Aircraft altitude is the provider's barometric altitude when available, with geometric altitude as a fallback; it is not height above the camera or terrain.

## Feed and freshness

The Controller queries each distinct configured area about every ten seconds, shared by all tiles and overlays using that area. Up to four areas are supported. Positions older than 60 seconds are removed; a feed older than 30 seconds is marked outdated, and after 90 seconds its aircraft are hidden. Errors and rate limits slow retries. Coverage and aircraft metadata depend on the provider. No API account is required for the current integration.

Coordinates and radius are sent to [ADSB.lol](https://www.adsb.lol/docs/open-data/api/); place searches use the existing Open-Meteo geocoder. ADSB.lol lists its API data under ODbL 1.0. Every view includes attribution. Editor sample aircraft are explicitly labelled; unavailable live views never substitute sample traffic.

## Beta and rollback

This work starts from current `origin/codex/beta` / `origin/main` commit `c72f5a6`. Its application source matches release `v1.0.46` (`b04de0a`); changes after that tag were documentation, screenshots, and documentation fixtures. The aircraft build is `1.0.47-beta.1`.

Settings schema 17 adds aircraft options. Existing installations start with no aircraft widgets. The first save retains `settings.json.before-aircraft.json` when upgrading an older schema. Stop the beta before restoring that file for an older release; older releases cannot load schema 17. Normal configuration export/import includes aircraft settings.
