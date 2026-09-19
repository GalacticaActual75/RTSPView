# Beta.6: layout saves and Settings layout

- Settings readers allow atomic replacement of the current configuration. Saves retry brief Windows sharing violations without deleting the live settings file. A persistent lock still fails and preserves the last applied configuration.
- Request errors include an exception code and fixed route template in sanitized logs. Sharing conflicts return a retryable response instead of the generic error alone.
- LAN address Copy buttons have consistent spacing and wrap cleanly on narrow screens.
- Maintenance commands use equal-width button/description groups. Scheduled restarts have compact forms, weekday rows, and readable status fields.

Validation: a deterministic settings-reader lock reproduced an IOException before the fix and passes afterward; persistent-lock preservation, real authenticated layout appearance PUT/readback, native border/letterbox pixels and playback/layout regressions pass. Browser checks cover Copy, 16px address/button spacing, desktop Maintenance rendering, and 390px layouts without horizontal overflow.

The reported host log contains IOException during Apply failures, consistent with the reproduced lock failure; the old log does not record the underlying OS code. The updated diagnostics will distinguish any other I/O failure if it persists.
