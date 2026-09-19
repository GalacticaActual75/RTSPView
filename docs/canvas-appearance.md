# Canvas appearance — 1.0.43-beta.5

Standard and automation layouts now have Borderless view, Border color, and Background color under Canvas settings. Each layout saves its own appearance. Background fills empty canvas space and letterboxing in native and composited playback, as well as web previews. Black bars encoded into camera footage remain part of the image.

Existing layouts retain the global Display border preference until overridden. Use display border setting restores inheritance. Standard appearance returns when automation focus expires. Floating overlay appearance remains separately controlled.

Validated with configuration persistence/validation, automation resolution and restoration checks, native host pixel checks for custom letterbox colors, full native playback/layout regressions, and browser save/reload checks for both layout types. Release build passes with zero warnings/errors.
