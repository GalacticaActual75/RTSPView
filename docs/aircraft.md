# Aircraft widgets and tiles

Aircraft views show airborne traffic around a configured location using ADSB.lol. They share the weather widget's dark/light surface, opacity, corners, padding, accent, font and icon controls.

## Add to a camera

In **Layouts**, select a camera tile and choose **Aircraft Widget**. Search for a city, enter coordinates, or choose **Use weather location**. Set the radius and optional altitude limits, then choose the position and size. **Save & apply** saves the widget for that camera across standard and automation layouts. The camera keeps playing behind it. Weather and aircraft widgets have independent positions; aircraft defaults to bottom right, weather to bottom left.

Aircraft widgets stay visible like weather widgets. An empty feed shows **No aircraft nearby**; unavailable or failed data remains visibly labelled, with the feed error when available. Enable **Hide when no aircraft nearby** to show the widget only when fresh aircraft positions match the radius and altitude filters. Waiting, empty, stale and unavailable states remain hidden while this option is enabled. Uncheck **Enable Aircraft Widget** to remove it from the camera. Conditional tile replacement still returns to the camera when no fresh aircraft match.

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

## Beta.2 updates

Camera tiles now offer **Replace while aircraft nearby**. This keeps the camera assigned and decoding, shows the aircraft card only for fresh matching traffic, and returns to the camera on empty, stale or failed data. Existing dedicated aircraft tiles can choose a return camera in their tile panel and select **Use conditional replacement**. Standalone tiles remain supported.

Photos use Planespotters.net when available, with photographer credit. Registered owner, airline and destination use adsbdb. Enable the fields under **Choose aircraft information** for existing widgets. Destination is a callsign lookup, not a confirmed live flight plan. New coordinates retain full precision. Aircraft identifiers/callsigns are sent to these services for enabled metadata; lookups are cached and run independently of position updates. Photo and lookup failures leave the flight metrics available.

Beta.2 uses schema 18 and saves settings.json.before-aircraft-details.json before the first upgrade save. Restore that backup after stopping beta.2 to return to beta.1.

## Beta.3 updates

Aircraft supports three displays: a persistent corner widget, an aircraft card over the camera only while matching traffic is nearby, and a permanent aircraft tile. The camera-backed card honors background opacity: 0% keeps the video visible behind the text; 100% covers it. The selected-tile panel groups these choices below the camera selector.

Live View now remeasures after hiding optional details, preserving the flight callsign on smaller tiles. Unchanged feed refreshes retain the rendered card. Feed failures include a diagnostic reason, and widgets stay visible when traffic is empty. Conditional cards still return to the camera when traffic is empty, expired, or unavailable.

## Pending beta updates

Flight boards use the full card width for one aircraft and two equal columns for multiple aircraft. The registered owner is the heading, followed by aircraft model/type and tail number. Full model names are used when available; photos remain dependent on Planespotters coverage.

An in-progress request retains the previous fresh aircraft result. Unchanged native polls preserve the visual tree, and browser updates reuse loaded aircraft images. A completed empty result removes departed aircraft. This does not extend freshness limits: with hide-when-empty enabled, expired positions and stale or failed feeds remain hidden.
