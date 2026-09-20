# Automation review — 2026-09-20

Reviewed the current beta checkout at `69fd1a2`, not the older workspace-root source. This document records the findings **before implementation**. Severity describes realistic user impact; no Critical defect was established.

## A. Functional / automation issues

### F1 — High: one invalid target disables unrelated rules and can prevent disabling the integration

**Trigger:** Disable a main camera used by a focused-layout rule, clear a referenced stream URL, or import settings that remove a referenced layout. Ordinary deletion endpoints guard references, but camera edits and configuration import can invalidate them. Both services validate the entire rule collection before processing it. Validation also checks references in disabled rules and when the integration is disabled.

**Impact:** Unrelated valid rules stop. MQTT reports an integration error; Tapo can report healthy hub polling or silently present no effects depending on the path. Turning off the integration or just the offending rule can fail validation, forcing users to repair/delete references first.

**Correction:** Separate structural validation from target availability. Permit disabling invalid rules/integrations, identify the failing rule and field, and isolate invalid active rules at runtime. Keep reference guards for destructive edits and validate import dependencies. Do not silently substitute another camera.

**Components:** `Core/Automation.cs` (`Validate`, `CanFocus`), `Core/TapoAutomation.cs` (`Validate`), `Controller/AutomationService.cs` (`ExecuteAsync`), `Controller/TapoService.cs` (`PollAsync`, `PresentAsync`), `Controller/Program.cs` (camera edits/import).

### F2 — High: Tapo viewer failure is absent from operational status

**Trigger:** A hub remains reachable while the viewer is stopped, unresponsive, or rejects a configuration hash. `PresentAsync` populates `ActiveRules` from desired effects before sending them; unsuccessful results are returned to test callers but not stored in `TapoStatus`. The background loop discards exceptions.

**Impact:** The page reports a connected integration and an active condition while nothing changed on the wall. There is no last delivery error or success time to diagnose it.

**Correction:** Report sensor health independently from delivery health. Persist command attempt/result/time and distinguish a matching condition from acknowledged presentation. Retain errors through polling and provide a viewer-status next action.

**Components:** `Controller/TapoService.cs`, `wwwroot/tapo.js`, `Controller/ViewerCommandClient.cs`.

### F3 — Medium: acknowledgement is not evidence that a particular rule is visible

**Trigger:** Multiple rules compete for the same view/overlay, an MQTT lease was manually dismissed, or Tapo runs under manual camera focus. Viewer application can return success for a whole presentation while only its priority winner is effective. MQTT records successful delivery for every lease in the presentation. Its test endpoint replaces the viewer's successful message with a generic message.

**Impact:** “Last triggered,” “Test active,” and a successful test can be interpreted as a visible layout change even when another rule wins. Tapo explicitly acknowledges under manual focus.

**Correction:** Label existing success as “Viewer acknowledged,” not “Layout changed.” A later viewer telemetry extension should report the effective rule IDs and suppression reason. Tests must retain the viewer message and say they bypass the physical event source. Pixel/video success cannot be inferred from a named-pipe acknowledgement.

**Components:** `Viewer/AutomationOverlays.cs`, `Core/OverlayAutomationState.cs`, `Core/TapoAutomation.cs` (`SensorPresentationState`), `Controller/AutomationService.cs`, `Controller/Program.cs` test endpoint, both UI files.

### F4 — Medium: stale per-rule status and disappearing test errors

**Trigger:** An Automation status request fails after a rule was active. Both frontends replace the integration heading but retain old rule text. Tapo test output shares the same element as periodic status, so its failure is overwritten on the next successful status fetch. MQTT's single LastResult can be overwritten by an unrelated ignored event or reconnection message.

**Impact:** A stale active/countdown status or “Test active” can outlive reliable evidence, and a useful failure vanishes before it can be read.

**Correction:** Mark rule state unknown when freshness is lost, separate durable test feedback from runtime status, and give command failure its own field rather than overloading event parsing status.

**Components:** `wwwroot/automation.js` (`refresh`), `wwwroot/tapo.js` (`refresh`, `test`), `Controller/AutomationService.cs`.

### F5 — Medium: MQTT draft flag and priority coordination disagree

**Trigger:** Add or delete a MQTT rule using its button. These handlers set the closure's `dirty` flag but do not consistently set `form.dataset.dirty`, which Priority uses. Browser reproduction: after Add rule, one draft rule exists while `automationForm.dataset.dirty` is still `false`. There is also no optimistic revision check on normal MQTT/Tapo whole-settings saves.

**Impact:** Priority can be saved while MQTT drafts exist. Saving that older draft afterward can restore old priorities. A second admin page can overwrite newer settings without warning.

**Correction:** Centralize dirty tracking for every mutation, coordinate pending order changes with rule edits, and introduce configuration revision checking for whole-settings saves. Keep drafts intact on conflicts.

**Components:** `wwwroot/automation.js`, `wwwroot/automation-tabs.js`, both save endpoints/services.

### F6 — Medium: connection test does not test the saved rule subscriptions

**Trigger:** The MQTT UI always sets `request.settings.rules = []` before invoking connection test. The service supports subscription validation, but it never sees the UI's rule topics.

**Impact:** A broker can accept credentials but reject access to a rule's topic; Test connection passes and automation later fails to subscribe.

**Correction:** Keep a connection-only test usable with incomplete drafts, label its scope explicitly, and separately test saved enabled subscriptions. Do not claim real event parsing was tested by a rule-action simulation.

**Components:** `wwwroot/automation.js` (`mutate`), `Controller/AutomationService.cs` (`TestAsync`, `Subscribe`).

### F7 — Medium: startup settings error can be hidden; automation backup is incomplete

**Trigger:** Malformed `automation.json` makes the constructor set Error but leaves default disabled settings; the worker then overwrites the error with “Automation disabled.” Tapo preserves an error longer but its UI still loads default settings. Automation files have atomic temporary-file replacement but no rolling recovery backup. Normal configuration export covers AppSettings, not MQTT/Tapo rules and hubs.

**Impact:** Lost/unreadable rules can look like an intentionally empty disabled integration. A normal configuration export is not a complete automation backup.

**Correction:** Latch load errors until a successful explicit save; preserve the damaged file before recovery; add credential-free automation backup/export with explicit scope and coordinated restore validation. Never export decrypted passwords.

**Components:** both service constructors/persistence, `Infrastructure/JsonSettingsStore.cs`, `Controller/Program.cs` export/import.

### F8 — Medium: priority saves reset detection episodes and reconnect MQTT

**Trigger:** Saving the combined order calls full MQTT SaveAsync, even when MQTT priorities did not change. Every revision clears leases/history and disposes the broker client. It also saves Tapo and cancels current tests. The two files are written sequentially with rollback for a caught error, not a crash-atomic transaction.

**Impact:** An active person view disappears on reordering and requires a new detection to return; history resets and a brief event reception gap occurs. A process crash between writes can leave an order only partly applied.

**Correction:** Treat priority-only updates separately: preserve episode identity/deadlines, update priorities in existing leases, avoid broker reconnects, and persist the shared order atomically or journal the transaction. Display pending order separately from applied numeric priority.

**Components:** `Controller/AutomationPriorityEndpoints.cs`, `Controller/AutomationService.cs` revision handling, `Controller/TapoService.cs` persistence, `wwwroot/automation-tabs.js`.

### F9 — Medium: MQTT save/test can discard edits made while a request is running

**Trigger:** MQTT disables buttons and fieldsets during a request, but the master toggle is outside those fieldsets. It remains editable. A successful save rebuilds the form from the request's earlier snapshot.

**Impact:** A user toggling automation during a slow save can lose that newer choice; the displayed switch reverts on completion. Tapo already disables its inputs during mutation.

**Correction:** Freeze the whole form consistently while saving/testing or preserve a draft generation and reconcile responses. Show saved enabled state separately from pending changes.

**Components:** `wwwroot/automation.js` (`mutate`, `render`).

### F10 — Low: insufficient durable diagnostic history

**Trigger:** Routine saves reset MQTT last-event/trigger history; restart loses all runtime history. The main log records saves and local test requests but lacks per-rule decisions, priority winner and delivery result. Tapo has no per-rule last-trigger timestamps. MQTT's DropOldest queue does not expose dropped event counts.

**Impact:** Intermittent misses are difficult to reconstruct. A user cannot distinguish no event, zone rejection, priority suppression and failed delivery after the fact.

**Correction:** Add a bounded sanitized activity history with rule ID/name, event decision, delivery result and timestamps. Log transitions/errors without raw payloads or credentials; expose dropped-event counts. Avoid a noisy log line for every Tapo renewal.

**Components:** both services, `MqttDiagnostics.cs`, logging/status models, both frontends.

### Existing behavior that should be preserved

- MQTT validates timestamps, rejects retained/stale/duplicate/out-of-order messages, matches exact topics and optional person zones, and processes events in a bounded single-worker queue.
- Fresh detections renew episode deadlines without changing episode identity; slower sources do not shorten a shared timeout. Multiple triggering-camera targets can have separate leases.
- Viewer-local lease expiry prevents an old controller timer from blindly restoring a layout over a newer owner. There is no independent per-rule delayed “restore” command to race a later trigger.
- MQTT manual dismissal persists through renewals of that episode. Priority 1 is highest; higher priorities preempt and still-active lower priorities may resume. Tapo wins equal cross-integration priority. Saving a unique total order removes most ties.
- Tapo reads current state on reconnect/startup and preserves missing sensors as Unavailable, never Closed. Its viewer presentation expires after five seconds without renewal. Polling failures have bounded helper timeouts.
- MQTT does not replay failed actions without a new detection; viewer restart does not replay old MQTT detections. Tapo deliberately reapplies the current state. These are different and valid recovery policies, but the UI should explain them.
- Clearing removes an override and reveals other active rules or the current saved layout/overlay setting. It is not a general saved snapshot of every previous display state.
- Reference-aware deletion, encrypted credentials, authenticated API endpoints and current-user-only named pipes are already in place. Camera rename preserves slot identity. Zone rename cannot be detected without new external events.
- There is one local viewer controlled by named pipe. Do not invent a viewer picker, remote delivery, multiple viewers or a global master switch that the backend does not support.

## B. UI / UX inconsistencies

| Finding | Existing reference to reuse | Proposed correction |
|---|---|---|
| MQTT summary is a long technical sentence and omits the selected layout name; Tapo exposes every editor simultaneously. | Streams inventory (`product.css` `.stream-row`, `workspace.js`), existing MQTT disclosure. | Same compact collapsed rule presentation for both integrations; name, trigger, action/target, duration/afterward, runtime status. Expand one editor when needed. |
| Tapo inventory and explanatory paragraphs precede rules, while MQTT is rules-first. | Streams toolbar and Settings progressive disclosure. | Put saved rules first; sensor inventory beside/inside connection and diagnostic disclosure. Surface only health problems above rules. |
| Integration enabled state is mixed with a pending checkbox value; no persistent dirty indication near it. | Streams saved state and Overlays sticky action row. | Show saved operational state and a distinct “Unsaved changes” message; uniform Save changes / Discard changes. |
| Errors, waiting, disabled and timestamps use the same muted text. | Monitor and `adminUi.stream` use `data-tone`; `product.css` already defines healthy/warning/error and status-label. | One short operational state plus actionable supporting text. Unknown/stale must not look active. |
| Automation toggle is a custom 44px blue pill instead of the shared 36px green track. | Settings `.toggle-control` / `.toggle-track`, Streams `.switch`. | Reuse shared toggle markup and keyboard focus treatment. |
| Automation tabs stretch across 1100px; Settings and inspector tabs use compact labels and shared selection treatment. | Settings `.system-tabs`, Overlays `.inspector-tabs`. | Keep the integration split but use compact tabs, the established text weight, colors and spacing. |
| Automation CSS reintroduces rounded card backgrounds and 18px rule headings after product.css flattened them. Tapo uses another fieldset variant. | Product tokens, flat Streams rows, Settings section dividers. | Consolidate the conflicting Automation selectors, use tokenized radii/colors and flat divided rows. |
| Test controls lack a durable outcome; Tapo polling overwrites test messages. | Shared inline status/error semantics, viewer control feedback. | Separate test progress/result from live condition and clearly state simulation scope. |
| Initial MQTT state offers Add rule/Edit layouts with no sequence and keeps connection settings closed. Empty target selectors allow users to start an impossible rule. | Streams empty-state and focused editor. | Explain the next missing prerequisite and provide a direct setup action: connection, configured camera/overlay, event mapping, then rule. |
| Rules consume vertical space with routine explanations, raw slot IDs, zone help and takeover policy. | Settings help disclosure and Stream advanced settings. | Keep concise WHEN/DO/UNTIL/AFTER fields visible; move technical details and conflict policy into Advanced. |
| MQTT save row is sticky; Tapo save row is not, and labels differ. | Overlays actions and Streams drawer actions. | Consistent sticky form-level save row, no misleading per-rule “saved” claim before form save. |
| Narrow layouts stack many form controls without a concise summary. | Streams two-column mobile rows and shared breakpoint behavior. | Stack labeled summary values on narrow screens, allow tab overflow, wrap actions and long names/topics, retain keyboard access. |

## C. Code / component inconsistencies

The application is vanilla JavaScript and CSS, not a component framework. Reuse actual shared primitives rather than introduce a framework. `product.css` is the live design reference; historical `app.css`, `layout.css` and `compact.css` are not the primary stylesheet loaded by the current index.

`automation.css` contains repeated declarations for grid, source rows, rule summaries, previews, form widths and savebars; late ID-specific selectors override product-level rules. MQTT and Tapo separately implement option preservation, dirty/busy state, status rendering, test controls and rule layout. Use a small shared Automation presentation helper for common rule summary/test/status behavior, the existing product tokens and status attributes, and retain source-specific data handling. Settings already supplies accessible tab behavior; both Automation and Settings can use the same styling primitive without restructuring unrelated pages.

## D. Recommended UX structure (before implementation)

1. **Information architecture:** Retain MQTT, Tapo and Automation Priority tabs. They represent genuinely different trigger models and separately saved integrations. Give MQTT and Tapo the same internal order: saved enabled/health line, rules toolbar and rows, connection settings, diagnostics, sticky save row. Do not merge into one generic rule schema in this cleanup. Keep layout geometry in the existing Layouts editor with a direct link.
2. **Rule row:** Name and condition/delivery status on top; labeled Trigger, Action, Target and Duration/After values beneath. Include saved layout names and camera names; show numeric IDs only for ambiguity or expanded technical details. Last detected/matched and last acknowledged must be separate. No metric cards or badge collection.
3. **Create/edit:** Keep inline expanded editing as the least disruptive fit for the existing form-wide save model. New rules open automatically and focus Name; existing rules collapse to summaries. Use WHEN → DO → UNTIL/AFTER → Advanced. Do not introduce a modal whose Save implies a separately persisted rule. A future drawer needs true per-rule draft/save semantics first.
4. **MQTT editor:** WHEN Person detected on selected mapped cameras; optional zone filter near each source; DO Overlay / Fullscreen / Automation layout and camera(s); UNTIL no new detections for N minutes; AFTER remove this override and resume other active rules or saved wall. “On this RTSPView wall” is explanatory text, not a fake target selector.
5. **Tapo editor:** WHILE selected sensor is Open/Closed; DO action with conditional target fields; WHEN opposite state choose an action; IF UNAVAILABLE choose recovery behavior. No invented duration: state stays effective while it is observed. Keep unavailable behavior visible because it is an operationally important choice.
6. **Status/errors:** Separate configuration enabled from integration health and viewer delivery. State vocabulary: Disabled, Waiting for detection, Condition matched, Viewer acknowledged, Waiting behind another rule (only with viewer evidence), Viewer unavailable, Status unavailable, Invalid rule. Include a concrete next step, e.g. “Sensor readings are available, but the viewer did not respond. Check Live View in Quick actions.” Neutral “Not triggered yet” is not an error. Do not promise decoded video from command acknowledgement.
7. **Advanced:** MQTT exact topic entry, client ID, raw messages, discovery prefix, equal-priority takeover policy, Tapo poll interval and verbose connection diagnostics. Keep connection setup accessible and open when absent. Keep zones available in WHEN with a compact optional filter; do not bury a regularly used trigger constraint. Priority order remains in its existing dedicated tab, linked from Advanced.
8. **Terminology:** “Focused layout” → “Automation layout” (with brief emphasis explanation); “Required zone” → “Zone filter (optional)”; “Clear” → “No detections for…” for MQTT and “When door closes/opens” for Tapo; “Restore normal behavior” → “Resume other rules / saved wall”; “Controller unavailable” → “RTSPView service unreachable — status cannot be refreshed”; “Viewer command” → “Viewer acknowledged” where that is the actual evidence. Keep MQTT, Tapo and Priority, which are useful real concepts.

```text
Automation
MQTT                 Tapo                 Automation Priority
────────────────────────────────────────────────────────────
MQTT automations                       Enabled (saved)
Broker connected · Viewer last acknowledged 14:32

Rules                                      + Add rule
────────────────────────────────────────────────────────────
▸ Front door focus                    Waiting for detection
  Trigger   Person · Front door
  Action    Automation layout · Entrance large view
  Target    Front door · This RTSPView wall
  Duration  1 min without detections → resume other rules/wall
  Last detection 2 min ago · Viewer acknowledged 2 min ago
                                                Test action
────────────────────────────────────────────────────────────
▸ Back yard overlay                   Disabled
  Trigger   Person · Back yard     Action   Show Yard overlay

▸ Connection settings                         Broker configured
▸ Find camera events & troubleshoot

Unsaved changes                      Discard changes   Save changes
```

This wireframe illustrates hierarchy, not new backend capabilities. The saved-enabled text remains unchanged until Save succeeds. Error text replaces neutral status near the affected rule/integration, not only in diagnostics.

## E. Proposed implementation plan and verification

1. Add reproducible regression coverage for F1–F9; establish richer delivery status and load-error handling before relying on those values in the UI.
2. Correct dirty/busy handling, durable test feedback and honest test scope. Add configuration revision checks and priority-only update handling with episode preservation. Keep all current action types and priority semantics.
3. Apply the shared compact rule presentation and inline editing pattern to both integrations. Consolidate Automation CSS against product primitives; avoid unrelated screen changes.
4. Add targeted invalid-reference recovery and sanitized bounded activity history; document backup scope and implement an explicit credential-free automation export/restore path rather than silently changing the existing backup format.
5. Verify real local MQTT → service → named-pipe acknowledgements; retained/repeated/stale events; simultaneous/two-focus detections; unequal/equal priorities; manual focus and dismissal; timers after preemption; broker and viewer failures; restart; disable with invalid references; priority save during active episodes; stale page revisions; draft preservation; Tapo Open/Closed/Unavailable and blocked reader.
6. Exercise desktop and narrow layouts, keyboard editing/tabs/disclosures, long labels, empty prerequisites, loading, disabled, disconnected and failed test states. Check frontend console errors and backend builds. Physical H100/H200/T110 and live camera delivery require hardware verification; mocks are not that verification.

### Review evidence collected

- Baseline `RTSPView.AutomationChecks` passed using the repository's .NET toolchain: parser/freshness/duplicate/zone-related validation, OR renewal, expiry, priority, manual override, encrypted persistence, real local MQTT and named pipe, broker outage/reconnect/disable, and diagnostic discovery.
- Baseline `RTSPView.TapoChecks` passed: state evaluation, cross-integration priorities, persistence, polling, partial/offline state, tests/disable/account removal, and packaged helper protocol without live hubs.
- Browser inspected the real current index and styles through an isolated synthetic fixture. Confirmed MQTT/Tapo hierarchy divergence and reproduced Add rule leaving the shared dirty flag false. No installed configuration or physical cameras/hubs were modified.
- Review is source-backed; reported faults not explicitly reproduced are identified by the concrete code paths above. No claim of live hardware validation or pixel-confirmed layout delivery is made.

## Implementation outcome

The audit and wireframe above were presented before implementation. The following focused changes were then made locally in this checkout; nothing was published or installed.

| Finding | Implemented correction / remaining boundary |
|---|---|
| F1 | Runtime validation isolates invalid active rules in both integrations and reports the affected rule. Target availability no longer prevents saving an integration/rule as disabled. Structural validation remains strict. Combined imports validate included active automation references against the imported configuration before writing. |
| F2 | Added delivery attempt time, result and message to operational status. Tapo exceptions/failures remain visible separately from healthy sensor connectivity. MQTT also retains per-rule acknowledgement results. |
| F3 | Viewer telemetry now reports effective rule IDs and reasons for suppression by priority, manual focus, dismissal, fullscreen or a hidden viewer. Both rule lists distinguish viewer priority from command acknowledgement and mark missing/stale telemetry unknown. This reports arbitration, not successful camera decoding. |
| F4 | Failed polling replaces old per-rule activity with Status unavailable. Tapo test output has a separate persistent message, untouched by runtime polling. MQTT command outcomes no longer rely solely on LastResult. |
| F5 | All MQTT mutations share dirty tracking. Both forms send a settings revision; outdated browser saves are rejected without replacing the draft. Revision remains optional for backward-compatible API callers; clients omitting it do not receive this protection. |
| F6 | With no draft edits, MQTT connection testing includes saved enabled rule subscriptions. Incomplete drafts still allow a connection-only test and explicitly say subscriptions were not checked. Subscription testing does not require valid display targets. |
| F7 | Both services retain unrecoverable configuration errors until an explicit successful save and preserve damaged files. Ordinary saves retain three local generations; startup validates and restores the newest valid backup, recording recovery in activity. MQTT/Tapo settings are included in ordinary web configuration export/import. |
| F8 | Priority-only updates preserve MQTT lease IDs/deadlines, broker connection and history, and retain Tapo test state. Tapo saves are serialized against presentation sends. A flushed recovery journal protects shared order saves and combined imports: interruption before commit restores all previous files and backup generations before services start. |
| F9 | The MQTT form is inert during save/test, including its master switch; later responses cannot discard a newly toggled value during the request. |
| F10 | Each integration retains its last 200 sanitized activity entries across restarts: rule identity, decisions, action requests, delivery outcomes and viewer priority transitions. Raw payloads and passwords are excluded. Repeated decisions are grouped and unchanged Tapo renewals are omitted. MQTT reports queue overflow counts since Controller start and records counts in activity. Tapo displays the latest action timestamp retained in history. |

UI implementation preserves the three tabs, dark palette and compact controls. Both integrations now have flat, divided rule summaries with Trigger / Action / Target / Duration or Afterward values, saved enabled state separate from drafts, collapsible inline editors, shared Settings tab styles and toggle primitives, status tones, and sticky Save changes / Discard changes actions. New rules focus Name. Tapo inventory moved into its connection disclosure; equal-priority MQTT behavior and Tapo polling interval moved into Advanced. MQTT setup opens automatically when no broker is configured. Layout names appear in summaries. Existing action enums, timing limits, priority direction and manual-focus arbitration are unchanged.

Changed implementation files: `AutomationService.cs`, `TapoService.cs`, new `AutomationDeliveryStatus.cs`, the MQTT test response in `Program.cs`, `Core/Automation.cs`, `Core/TapoAutomation.cs`, `automation.js`, `tapo.js`, new `automation-presentation.js`, `automation-tabs.js`, `automation.css`, and script/style references in `index.html`. Added regressions to the existing AutomationChecks and TapoChecks executables.

### Final validation

- Full solution Release build: **passed, zero warnings/errors**, using the local .NET toolchain. The system `dotnet` installation has no SDK. Cached restores were used with NuGet audit disabled for these local runs only; no repository audit configuration changed.
- AutomationChecks: **passed**, including new active-priority episode preservation, stale-save conflict, invalid-rule isolation, disable with invalid references, subscription-test target independence, and persistent load-error/recovery cases. Existing real local MQTT/named-pipe, retained/duplicate/stale events, reconnection, expiry, manual-dismissal and focus-layout checks also pass.
- TapoChecks: **passed**, including new failed/recovered delivery status, priority preservation of simulations, stale-save conflict, invalid-rule isolation, disabling invalid references and damaged-settings recovery. Packaged helper protocol passed without physical hubs.
- ConfigurationChecks and native UiChecks: **passed**, including actual viewer sensor overlay/layout handling, saved-wall restoration, higher-priority person preemption, and rejected stale configuration.
- Tapo HTTP checks: **passed** for authentication/setup/CSRF, secret persistence/removal, action/priority persistence and validation.
- JavaScript syntax checks, related-choice regressions, snapshot/dashboard checks and diff whitespace check: **passed**.
- Isolated browser fixture: tested empty setup, populated MQTT/Tapo rules, Add/edit/save/discard, new-rule keyboard focus, arrow-key tabs, saved/unsaved state, viewer failure, service outage, stale-state replacement and durable failed-test feedback. No browser console errors were recorded. At 390×844 both integrations fit without horizontal page overflow. No real credentials, hubs or installed settings were used.

Remaining verification: physical H100/H200/T110 behavior, production MQTT topic permissions and real decoded camera output on the user's display. Acknowledged presentation is deliberately not represented as proof that a particular rule won arbitration or that live video decoded successfully.

### Approved beta follow-up

The user's follow-up selected the existing configuration workflow for automation backups. Web Settings now exports MQTT and Tapo rules, priorities, source mappings, zones, hubs and connection identities in the regular JSON file. Passwords are excluded. Import validates against the imported display configuration before writes, retains compatible existing credentials, disables integrations needing new passwords, and preserves automation settings when loading older files without an automation section. The local pre-import backup also includes automation settings. Ordinary failures roll back captured settings and encrypted credential state; the recovery journal also restores interrupted imports before services start. The legacy native Viewer stream editor remains outside this web workflow.

The supplied coffee artwork and a speech-bubble feedback button now form a fixed bottom-left pair on all six web administration pages. Coffee is above feedback and uses the existing support URL. Feedback opens the existing dialog. Buttons have accessible names, visible keyboard focus and mobile spacing; they are hidden with the application before sign-in.

Follow-up validation: full Release build passed with zero warnings/errors. Extended HTTP checks passed combined export/import, password omission and compatible retention, changed-account disabling, invalid-reference rejection, legacy imports and combined local backup. JavaScript syntax checks passed. Browser checks confirmed both buttons on all six pages, dialog opening and focus restoration, and no horizontal overflow at 390×844.

### Review completion

Completed the remaining software work for F3, F7, F8 and F10. The recovery journal covers current configuration and backup generations, uses fixed local filenames, flushes its contents before any live-file mutation, and is removed only at commit. Startup recovery is idempotent. It protects against Controller interruption; it does not promise protection against damaged storage or concurrent external editing of data files.

Added regression checks for interruption versus commit, rollback of newly created files and rolling backups, fallback past a damaged backup, preservation of corrupt originals, history bounds/sanitization/restart retention, unchanged-state deduplication, MQTT versus Tapo winners, equal-priority Tapo precedence, manual focus, dismissal, hidden viewers and expiry. Full solution build, AutomationChecks, TapoChecks, native UiChecks and web HTTP checks passed. Browser validation covered both activity disclosures, winning/suppressed status, action timestamps, queue counts and 390×844 layout with no horizontal overflow or console errors.

All identified software corrections are implemented in the local beta checkout. Physical H100/H200/T110 behavior, production broker permissions and real camera decoding remain deployment-environment verification; synthetic and native checks do not substitute for that hardware verification. No installer has been published.
