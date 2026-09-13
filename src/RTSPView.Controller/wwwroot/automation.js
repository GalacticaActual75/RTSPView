/* All event processing runs in Controller. This tab only edits configuration. */
const automationUi = (() => {
  let form, rules, message, connection, inventory = [], overlays = [], loaded = false, dirty = false, busy = false, refreshing = false;
  let savedRules = [], discovered = [], diagnosticState = null, paused = false, clearedThrough = 0, diagnosticBusy = false;
  const uuid = () => '10000000-1000-4000-8000-100000000000'.replace(/[018]/g, c =>
    (Number(c) ^ crypto.getRandomValues(new Uint8Array(1))[0] & 15 >> Number(c) / 4).toString(16));
  function init() {
    form = document.createElement('form'); form.id = 'automationForm'; form.className = 'panel control-panel';
    form.innerHTML = `<h2>Automation</h2>
      <p>When selected cameras detect a person, show an overlay, fill the viewer with a camera, or enlarge a camera while keeping the other streams visible. Runs while the browser is closed.</p>
      <p class="automation-connection" role="status">Loading connection status…</p>
      <fieldset class="automation-fields"><legend>MQTT connection</legend>
      <label><input name="enabled" type="checkbox"> Enable automation</label>
      <div class="automation-grid">
      <label>Broker host<input name="host" placeholder="Scrypted hostname or IP" maxlength="253" spellcheck="false"></label>
      <label>Port<input name="port" type="number" min="1" max="65535" required value="1883"></label>
      <label>Transport<select name="tls"><option value="false">TCP</option><option value="true">TLS (validate certificate)</option></select></label>
      <label>Authentication<select name="authenticate"><option value="false">None</option><option value="true">Username and password</option></select></label>
      <label>Username<input name="username" maxlength="256" autocomplete="off"></label>
      <label>Password<input name="password" type="password" maxlength="1024" autocomplete="new-password" placeholder="Not set"></label>
      </div><label><input name="clearPassword" type="checkbox"> Clear saved password</label>
      <details><summary>Advanced connection settings</summary><label>Client ID<input name="clientId" maxlength="64" required pattern="[A-Za-z0-9_-]+"></label>
      <p>Use a unique client ID for each RTSPView Controller. TLS uses the Windows trusted certificate store.</p></details>
      </fieldset><fieldset class="automation-fields"><legend>Person detection rules</legend>
      <p>Choose an action and one or more source cameras. Use Discover topics below to select each camera’s event topic. Manual topics remain available under Advanced.</p>
      <div class="automation-rules"></div><button type="button" class="secondary automation-add">Add rule</button>
      <p>Choose <b>Automation only</b> as the target’s Display mode in Overlays to hide it while waiting. Always visible overlays remain on screen after a rule clears.</p>
      <p>Clear means no new person detections; it does not prove the scene is empty. During a broker outage, the existing clear timer still expires.</p>
      <p>Fullscreen takes priority over focused layout. Competing cameras wait in detection order until earlier detections clear. Manual double-clicks override active automation; saved layouts are restored when focus clears.</p>
      </fieldset><section class="mqtt-tools" aria-label="MQTT discovery and details">
      <h3>MQTT discovery and details</h3><p>Listen using the connection fields above, even before saving a rule. Discovery does not trigger overlays. A listening session lasts five minutes.</p>
      <details><summary>Advanced discovery settings</summary><label>Topic prefix<input class="mqtt-prefix" value="scrypted" maxlength="400" spellcheck="false"></label></details>
      <div class="control-buttons"><button type="button" class="secondary mqtt-start">Discover topics / Start listening</button><button type="button" class="secondary mqtt-stop">Stop listening</button></div>
      <p class="mqtt-status" role="status">Not listening</p>
      <details class="mqtt-feed"><summary>Raw MQTT details</summary>
      <p>Received messages grouped by camera and topic. These are observations, not confirmation that a rule ran. Rule activity is shown above.</p>
      <div class="control-buttons"><button type="button" class="secondary mqtt-pause">Pause feed</button><button type="button" class="secondary mqtt-clear">Clear feed</button></div>
      <label>Filter camera or topic<input class="mqtt-filter" type="search" placeholder="Camera name or topic"></label>
      <div class="mqtt-messages"></div></details></section>
      <div class="control-buttons"><button type="button" class="secondary automation-test">Test connection</button>
      <button type="button" class="secondary automation-cancel">Cancel edits</button><button type="submit">Save automation</button></div>
      <p class="automation-message" role="status"></p>`;
    rules = form.querySelector('.automation-rules'); message = form.querySelector('.automation-message'); connection = form.querySelector('.automation-connection');
    document.querySelector('#page-automation').append(form);
    const markDirty = e => { if (!e.target.closest('.mqtt-tools')) { dirty = true; form.dataset.dirty = 'true'; } };
    form.addEventListener('input', markDirty); form.addEventListener('change', markDirty);
    form.addEventListener('invalid', e => {
      for (let parent = e.target.parentElement; parent && parent !== form; parent = parent.parentElement)
        if (parent.tagName === 'DETAILS') parent.open = true;
    }, true);
    form.querySelector('.mqtt-start').onclick = () => diagnostics(true);
    form.querySelector('.mqtt-stop').onclick = () => diagnostics(false);
    form.querySelector('.mqtt-pause').onclick = e => { paused = !paused; e.target.textContent = paused ? 'Resume feed' : 'Pause feed'; if (!paused) renderFeed(); };
    form.querySelector('.mqtt-clear').onclick = () => { clearedThrough = Math.max(clearedThrough, ...(diagnosticState?.messages || []).map(m => m.id)); form.querySelector('.mqtt-messages').replaceChildren(); };
    form.querySelector('.mqtt-filter').oninput = () => { if (!paused) renderFeed(); };
    form.querySelector('.mqtt-feed').ontoggle = () => { if (!paused) renderFeed(); };
    form.querySelector('.automation-add').onclick = () => { if (rules.children.length >= 32) return; addRule(); dirty = true; };
    form.querySelector('.automation-cancel').onclick = async () => { dirty = false; loaded = false; await load(); };
    form.querySelector('.automation-test').onclick = () => mutate(true);
    form.onsubmit = e => { e.preventDefault(); mutate(false); };
  }
  function optionSelect(items, value, label) {
    const select = document.createElement('select'); select.required = true; select.setAttribute('aria-label', label);
    const empty = document.createElement('option'); empty.value = ''; empty.textContent = `Select ${label.toLowerCase()}`; select.append(empty);
    for (const item of items) { const option = document.createElement('option'); option.value = item.slot; option.textContent = item.name; select.append(option); }
    if (value && !items.some(i => i.slot === value)) { const missing = document.createElement('option'); missing.value = value; missing.textContent = `Unavailable stream (${value})`; select.append(missing); }
    select.value = value || ''; return select;
  }
  function addSource(container, source = {}) {
    const row = document.createElement('div'); row.className = 'automation-source automation-grid';
    const cameraLabel = document.createElement('label'); cameraLabel.textContent = 'Source camera';
    const select = optionSelect(inventory, source.cameraSlot, 'Source camera'); select.className = 'source-camera'; cameraLabel.append(select);
    const cameraColumn = document.createElement('div'); cameraColumn.append(cameraLabel);
    addCameraPreview(select, cameraColumn);
    const topicLabel = document.createElement('div'); topicLabel.className = 'source-event';
    const chooserLabel = document.createElement('label'); chooserLabel.textContent = 'Detected camera / event topic';
    const chooser = document.createElement('select'); chooser.className = 'source-discovered'; chooserLabel.append(chooser); topicLabel.append(chooserLabel);
    const advanced = document.createElement('details'); advanced.innerHTML = '<summary>Advanced: exact MQTT topic</summary>';
    const manualLabel = document.createElement('label'); manualLabel.textContent = 'MQTT person-event topic';
    const input = document.createElement('input'); input.className = 'source-topic'; input.required = true; input.maxLength = 512; input.placeholder = 'scrypted/<device-id>/ObjectDetector'; input.value = source.topic || ''; input.spellcheck = false; manualLabel.append(input); advanced.append(manualLabel); topicLabel.append(advanced);
    chooser.onchange = () => { input.value = chooser.value; advanced.open = !chooser.value; if (!chooser.value) input.focus(); dirty = true; };
    input.oninput = () => updateSourceChoices(row, false);
    select.onchange = () => { input.value = ''; updateSourceChoices(row, true); };
    const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'secondary'; remove.textContent = 'Remove source'; remove.onclick = () => { row.remove(); dirty = true; };
    row.append(cameraColumn, topicLabel, remove); container.append(row); updateSourceChoices(row, true);
  }
  function addRule(rule = {id: uuid(), name: 'Person overlay', enabled: true, clearMinutes: 2, sources: []}, collapsed = false) {
    const card = document.createElement('fieldset'); card.className = 'automation-rule'; card.dataset.id = rule.id;
    card.innerHTML = `<legend>Person detection action</legend><div class="automation-grid">
      <label>Rule name<input class="rule-name" maxlength="100" required></label>
      <label><input class="rule-enabled" type="checkbox"> Enabled</label>
      <label>Action<select class="rule-action"><option value="0">Show overlay</option><option value="1">Fullscreen camera</option><option value="2">Focused layout</option></select></label>
      <label>Clear delay (minutes)<input class="rule-delay" type="number" min="0.1" max="120" step="0.1" required></label></div>
      <div class="automation-target-grid"><label class="target-label"></label><p class="target-help"></p></div>
      <div class="rule-sources"></div><div class="control-buttons"><button type="button" class="secondary source-add">Add source camera</button>
      <button type="button" class="secondary rule-remove">Delete rule</button></div><p class="rule-status" role="status">Not saved</p>`;
    const editor = document.createElement('details'); editor.className = 'rule-editor'; editor.open = !collapsed;
    const summary = document.createElement('summary'); summary.className = 'rule-summary'; editor.append(summary);
    for (const child of [...card.children]) if (child.tagName !== 'LEGEND' && !child.classList.contains('rule-status')) editor.append(child);
    card.querySelector('.rule-status').before(editor);
    if (collapsed) card.querySelector('.rule-status').textContent = 'Saved';
    card.querySelector('.rule-name').value = rule.name; card.querySelector('.rule-enabled').checked = rule.enabled;
    card.querySelector('.rule-delay').value = rule.clearMinutes;
    card.dataset.overlayTarget = rule.overlaySlot || ''; card.dataset.cameraTarget = rule.cameraSlot || 0;
    card.querySelector('.rule-action').value = rule.action || 0;
    renderActionTarget(card);
    card.querySelector('.rule-action').onchange = () => { renderActionTarget(card); dirty = true; };
    for (const source of rule.sources.length ? rule.sources : [{}]) addSource(card.querySelector('.rule-sources'), source);
    card.querySelector('.source-add').onclick = () => { if (card.querySelector('.rule-sources').children.length < 32) addSource(card.querySelector('.rule-sources')); dirty = true; };
    card.querySelector('.rule-remove').onclick = () => { card.remove(); dirty = true; };
    const updateSummary = () => {
      const action = card.querySelector('.rule-action'), target = card.querySelector('.rule-target');
      summary.textContent = `${card.querySelector('.rule-name').value.trim() || 'Unnamed rule'} · ${action.selectedOptions[0].textContent} → ${target.selectedOptions[0]?.textContent || 'Select a target'} · ${card.querySelector('.rule-delay').value} min${card.querySelector('.rule-enabled').checked ? '' : ' · Disabled'}`;
    };
    card.addEventListener('input', updateSummary); card.addEventListener('change', updateSummary); updateSummary();
    rules.append(card);
  }
  function renderActionTarget(card) {
    const action = Number(card.querySelector('.rule-action').value), label = card.querySelector('.target-label');
    const items = action === 0 ? overlays : inventory.filter(c =>
      (c.enabled && !overlays.some(o => o.slot === c.slot)) || (action === 1 && overlays.some(o => o.slot === c.slot)));
    const selected = action === 0 ? Number(card.dataset.overlayTarget) : Number(card.dataset.cameraTarget);
    const select = optionSelect(items, selected, action === 0 ? 'Overlay' : 'Camera to focus'); select.className = 'rule-target';
    if (action !== 0) { select.add(new Option('Camera that detected the person', '0'), 1); select.value = String(selected); }
    label.textContent = action === 0 ? 'Show overlay' : 'Camera to focus'; label.append(select);
    select.onchange = () => { card.dataset[action === 0 ? 'overlayTarget' : 'cameraTarget'] = select.value; };
    card.querySelector('.target-help').textContent = action === 0 ? 'Any selected source can show this overlay until all sources have been clear for the delay.' : action === 1 ? 'Fill the viewer with this camera, then restore the previous layout. Each triggering camera has its own clear timer when following detections.' : 'Enlarge this main camera and show all enabled, configured main streams around it. Restore the previous layout after the clear delay.';
  }
  function draft() {
    const f = form.elements;
    return { settings: {enabled: f.enabled.checked, host: f.host.value.trim(), port: Number(f.port.value), tls: f.tls.value === 'true',
      authenticate: f.authenticate.value === 'true', username: f.username.value, clientId: f.clientId.value,
      rules: [...rules.children].map(card => ({id: card.dataset.id, name: card.querySelector('.rule-name').value,
        enabled: card.querySelector('.rule-enabled').checked, action: Number(card.querySelector('.rule-action').value),
        overlaySlot: Number(card.querySelector('.rule-action').value) === 0 ? Number(card.querySelector('.rule-target').value) : 0,
        cameraSlot: Number(card.querySelector('.rule-action').value) !== 0 ? Number(card.querySelector('.rule-target').value) : 0,
        clearMinutes: Number(card.querySelector('.rule-delay').value), sources: [...card.querySelectorAll('.automation-source')].map(row => ({
          cameraSlot: Number(row.querySelector('.source-camera').value), topic: row.querySelector('.source-topic').value.trim()}))}))},
      password: f.password.value || null, clearPassword: f.clearPassword.checked};
  }
  function render(data) {
    const settings = data.settings, f = form.elements;
    f.enabled.checked = settings.enabled; f.host.value = settings.host; f.port.value = settings.port;
    f.tls.value = String(settings.tls); f.authenticate.value = String(settings.authenticate); f.username.value = settings.username; f.clientId.value = settings.clientId;
    f.password.value = ''; f.password.placeholder = data.hasPassword ? 'Saved — leave blank to keep' : 'Not set'; f.clearPassword.checked = false;
    savedRules = settings.rules;
    rules.replaceChildren(); settings.rules.forEach(rule => addRule(rule, true)); dirty = false; form.dataset.dirty = 'false'; loaded = true; refreshOverlayLinks();
  }
  async function load(config) {
    try {
      if (config) { overlays = [config.doorbellOverlay, config.garageOverlay, ...(config.additionalOverlays || [])].map(o => o.camera).filter(c => c.rtspUrl);
        inventory = [...config.cameras, ...overlays].filter(c => c.rtspUrl); }
      if (!dirty && !busy) render(await api('/api/automation'));
      await refresh();
    } catch (e) { message.textContent = e.message; }
  }
  async function mutate(test) {
    if (busy) return;
    for (const input of form.querySelectorAll('.source-topic')) if (!input.value.trim()) input.closest('details').open = true;
    // Testing a connection must also work while a new rule is incomplete.
    if (!test && !form.reportValidity()) return;
    busy = true; const request = draft(); if (test) request.settings.rules = [];
    for (const button of form.querySelectorAll('button')) button.disabled = true;
    for (const fieldset of form.querySelectorAll(':scope > fieldset')) fieldset.disabled = true;
    message.textContent = test ? 'Testing from Controller…' : 'Saving…';
    try {
      const result = await api(test ? '/api/automation/test' : '/api/automation', {method: test ? 'POST' : 'PUT', body: JSON.stringify(request)});
      if (test) message.textContent = result.message;
      else { render(result); message.textContent = 'Saved. Controller will apply these settings.'; }
    } catch (e) { message.textContent = e.message; }
    finally { busy = false; for (const button of form.querySelectorAll('button')) button.disabled = false; for (const fieldset of form.querySelectorAll(':scope > fieldset')) fieldset.disabled = false; }
  }
  async function refresh() {
    if (!loaded || refreshing) return; refreshing = true;
    try {
      const status = await api('/api/automation/status');
      connection.textContent = `${status.connection} · ${status.lastResult}${status.lastPerson ? ' · Last person: ' + new Date(status.lastPerson).toLocaleTimeString() : ''}`;
      for (const card of rules.children) {
        const state = status.rules.find(r => r.id === card.dataset.id), remaining = state?.expiresAt ? Math.max(0, Math.ceil((new Date(state.expiresAt) - Date.now()) / 1000)) : 0;
        const target = Number(card.querySelector('.rule-action').value) === 0 ? overlays.find(o => o.slot === Number(card.querySelector('.rule-target').value)) : null;
        const activeNames = (state?.activeCameraSlots || []).map(slot => inventory.find(c => c.slot === slot)?.name || `Stream ${slot}`);
        card.querySelector('.rule-status').textContent = (dirty ? 'Unsaved changes · ' : '') + (status.connection === 'Disabled' ? 'Automation disabled' : !state ? 'Not saved' : !state.enabled ? 'Disabled' : remaining ? `Detection active${activeNames.length ? ' · ' + activeNames.join(', ') : ''} · clear in ${remaining}s` : 'Waiting for person') + (target?.enabled ? ' · Target is Always visible; choose Automation only in Overlays to hide it while waiting.' : '');
      }
      if (!document.querySelector('#page-automation').hidden) await refreshDiagnostics();
    } catch { connection.textContent = 'Controller unavailable'; }
    finally { refreshing = false; }
  }
  function updateSourceChoices(row, suggest) {
    const input = row.querySelector('.source-topic'), chooser = row.querySelector('.source-discovered');
    const slot = Number(row.querySelector('.source-camera').value), camera = inventory.find(c => c.slot === slot);
    if (suggest && !input.value && camera) {
      const known = [...new Set(savedRules.flatMap(r => r.sources).filter(s => s.cameraSlot === slot).map(s => s.topic))];
      const matches = discovered.filter(t => t.cameraName?.toLowerCase() === camera.name.toLowerCase());
      if (known.length === 1 || matches.length === 1) { input.value = known.length === 1 ? known[0] : matches[0].topic; dirty = true; form.dataset.dirty = 'true'; }
    }
    const choices = discovered.map(t => ({value: t.topic, label: `${t.cameraName || 'Camera name unavailable'} · ${t.topic}${t.personSeen ? ' · Person observed' : ''}`}));
    if (input.value && !choices.some(c => c.value === input.value)) choices.unshift({value:input.value, label:'Configured topic · ' + input.value});
    const signature = JSON.stringify(choices);
    if (chooser.dataset.choices !== signature) {
      chooser.replaceChildren(new Option('Choose a discovered topic, or enter one under Advanced', ''));
      for (const choice of choices) chooser.add(new Option(choice.label, choice.value));
      chooser.dataset.choices = signature;
    }
    chooser.value = input.value;
  }
  function addCameraPreview(select, column) {
    const preview = document.createElement('div'); preview.className = 'automation-camera-preview';
    const image = document.createElement('img'); image.width = 160; image.height = 90;
    const caption = document.createElement('small'); caption.textContent = 'Latest snapshot';
    const refresh = document.createElement('button'); refresh.type = 'button'; refresh.className = 'secondary'; refresh.textContent = 'Refresh snapshot';
    preview.append(image, caption, refresh); column.append(preview);
    let generation = 0;
    function show() {
      generation++; preview.hidden = !select.value; image.hidden = true; caption.textContent = 'Loading snapshot…';
      if (!select.value) { image.removeAttribute('src'); return; }
      const name = inventory.find(c => c.slot === Number(select.value))?.name || 'Selected camera';
      image.alt = name + ' latest snapshot'; refresh.setAttribute('aria-label', 'Refresh snapshot for ' + name);
      image.onload = () => { image.hidden = false; caption.textContent = 'Latest snapshot · not live video'; };
      image.onerror = () => { image.hidden = true; caption.textContent = 'Snapshot unavailable'; };
      image.src = `/api/cameras/${Number(select.value)}/thumbnail?v=${Date.now()}`;
    }
    refresh.onclick = async () => {
      const current = generation, slot = Number(select.value); refresh.disabled = true;
      try { await api(`/api/cameras/${slot}/thumbnail/refresh`, {method:'POST'}); if (current === generation) show(); }
      catch(e) { if (current === generation) caption.textContent = e.message; }
      finally { refresh.disabled = false; }
    };
    select.addEventListener('change', show); show();
  }
  async function diagnostics(start) {
    if (diagnosticBusy) return; diagnosticBusy = true;
    const status = form.querySelector('.mqtt-status'); status.textContent = start ? 'Starting discovery…' : 'Stopping…';
    try {
      const request = draft(); request.settings.rules = []; request.settings.enabled = false;
      diagnosticState = await api('/api/automation/diagnostics/' + (start ? 'start' : 'stop'), {method:'POST', body:JSON.stringify(start ? {connection:request, prefix:form.querySelector('.mqtt-prefix').value.trim()} : {})});
      if (start) { discovered = []; clearedThrough = 0; paused = false; form.querySelector('.mqtt-pause').textContent = 'Pause feed'; }
      applyDiagnostics();
    } catch(e) { status.textContent = e.message; }
    finally { diagnosticBusy = false; }
  }
  async function refreshDiagnostics() {
    if (diagnosticBusy) return;
    diagnosticState = await api('/api/automation/diagnostics'); applyDiagnostics();
  }
  function applyDiagnostics() {
    discovered = diagnosticState.topics || [];
    form.querySelector('.mqtt-status').textContent = `${diagnosticState.connection} · ${discovered.length} object-event topics observed${diagnosticState.until ? ' · Session ends ' + new Date(diagnosticState.until).toLocaleTimeString() : ''}. If none appear, trigger camera activity and check that its MQTT extension is enabled.`;
    for (const row of form.querySelectorAll('.automation-source')) updateSourceChoices(row, true);
    if (!paused) renderFeed();
  }
  function renderFeed() {
    if (!form.querySelector('.mqtt-feed').open) return;
    const container = form.querySelector('.mqtt-messages'), filter = form.querySelector('.mqtt-filter').value.toLowerCase();
    const open = new Set([...container.querySelectorAll('details[open]')].map(d => d.dataset.key));
    const groups = new Map();
    for (const entry of [...(diagnosticState?.messages || [])].reverse()) {
      if (entry.id <= clearedThrough) continue;
      const base = entry.topic.slice(0, entry.topic.lastIndexOf('/'));
      const name = discovered.find(t => t.topic === base + '/ObjectDetector')?.cameraName || inventory.find(c => savedRules.some(r => r.sources.some(s => s.cameraSlot === c.slot && s.topic === base + '/ObjectDetector')))?.name || base;
      if (!`${name} ${entry.topic}`.toLowerCase().includes(filter)) continue;
      const key = `${name} · ${entry.topic}`;
      if (!groups.has(key)) groups.set(key, []); groups.get(key).push(entry);
    }
    container.replaceChildren();
    if (!groups.size) { container.textContent = 'No received messages match. Start listening and trigger camera activity.'; return; }
    for (const [key, entries] of groups) {
      const category = document.createElement('details'); category.dataset.key = key; category.open = open.has(key);
      const title = document.createElement('summary'); title.textContent = `${key} (${entries.length})`; category.append(title);
      for (const entry of entries) {
        const row = document.createElement('details'); row.dataset.key = String(entry.id); row.open = open.has(String(entry.id));
        const heading = document.createElement('summary'); heading.textContent = `${new Date(entry.received).toLocaleTimeString()} · Retained: ${entry.retained ? 'yes — not a live trigger' : 'no'} · QoS ${entry.qos}${entry.truncated ? ' · Payload truncated to 8 KB' : ''}`;
        const pre = document.createElement('pre'); try { pre.textContent = JSON.stringify(JSON.parse(entry.payload), null, 2); } catch { pre.textContent = entry.payload; }
        row.append(heading, pre); category.append(row);
      }
      container.append(category);
    }
  }
  function decorateOverlay(overlayForm) {
    const info = document.createElement('div'); info.className = 'overlay-automation-info';
    info.innerHTML = '<p class="overlay-mode-help"></p><p class="overlay-rule-links"></p><button type="button" class="secondary">Open Automation</button>';
    overlayForm.querySelector('.card-head').after(info);
    info.querySelector('button').onclick = () => adminLayout.select('automation');
    overlayForm.addEventListener('change', () => refreshOverlayLinks());
    overlayForm.addEventListener('input', () => refreshOverlayLinks()); refreshOverlayLinks();
  }
  function refreshOverlayLinks() {
    for (const info of document.querySelectorAll('.overlay-automation-info')) {
      const overlayForm = info.closest('form'), slot = Number(overlayForm.dataset.slot);
      info.querySelector('.overlay-mode-help').textContent = overlayForm.elements.enabled.value === 'false' ? 'Automation only: the feed stays connected in the background, hidden until a rule detects a person. Save overlay to apply this mode.' : 'Always visible: stays on screen even when automation is idle.';
      const linked = savedRules.filter(r => (r.action || 0) === 0 && r.overlaySlot === slot);
      info.querySelector('.overlay-rule-links').textContent = linked.length ? 'Linked rules: ' + linked.map(r => r.name + (r.enabled ? '' : ' (disabled)')).join(', ') : 'No automation rules assigned';
    }
  }
  function updateOverlay(camera) {
    overlays = overlays.filter(c => c.slot !== camera.slot); if (camera.rtspUrl) overlays.push(camera);
    inventory = inventory.filter(c => c.slot !== camera.slot); if (camera.rtspUrl) inventory.push(camera);
    refreshOverlayLinks(); refresh();
  }
  return {init, load, refresh, decorateOverlay, updateOverlay, refreshOverlayLinks};
})();
