/* Controller owns polling, credentials and actions. This form only configures them. */
const tapoUi = (() => {
  let form, config, sensors = [], dirty = false, busy = false, loaded = false, refreshing = false, draftDiscovery = false;
  const id = () => '10000000-1000-4000-8000-100000000000'.replace(/[018]/g, c =>
    (Number(c) ^ crypto.getRandomValues(new Uint8Array(1))[0] & 15 >> Number(c) / 4).toString(16));
  const q = selector => form.querySelector(selector);
  function select(label, items, value) {
    const wrap = document.createElement('label'); wrap.textContent = label;
    const input = document.createElement('select');
    items.forEach(([v, text]) => input.add(new Option(text, String(v))));
    if (value !== undefined && !items.some(([v]) => String(v) === String(value))) input.add(new Option('Unavailable selection', String(value)));
    input.value = String(value ?? items[0]?.[0] ?? ''); wrap.append(input); return wrap;
  }
  function input(label, cls, value, type = 'text') {
    const wrap = document.createElement('label'); wrap.textContent = label;
    const el = document.createElement('input'); el.type = type; el.className = cls;
    if (type === 'checkbox') el.checked = !!value; else el.value = value ?? '';
    wrap.append(el); return wrap;
  }
  function button(text, fn) { const b = document.createElement('button'); b.type = 'button'; b.className = 'secondary'; b.textContent = text; b.onclick = fn; return b; }
  function mark() { dirty = true; form.dataset.dirty = 'true'; }
  function init() {
    if (form) return;
    form = document.createElement('form'); form.id = 'tapoForm'; form.className = 'panel control-panel';
    form.innerHTML = `<h2>Tapo door sensors</h2>
      <p>Read T110 sensors directly through H100/H200 hubs. Home Assistant is not required.</p>
      <label class="tapo-enable"><input type="checkbox" name="enabled" role="switch" class="automation-toggle"> Enable Tapo sensor automations <span class="toggle-state" aria-hidden="true"></span></label>
      <p class="tapo-status" role="status">Loading sensor status…</p>
      <div class="tapo-inventory"></div>
      <h3>Sensor rules</h3><p>For an overlay only while a door is open, choose Open → Show overlay and When the state clears → Hide overlay. No layout change is needed. Restore normal behavior returns to the saved overlay setting and other active rules.</p>

      <div class="tapo-rules"></div><button type="button" class="secondary tapo-add-rule">Add sensor rule</button>
      <details class="tapo-connection"><summary>Tapo hubs and account</summary>
      <p>Add both hubs if you are unsure where a door sensor is paired. Use each hub's IPv4 address or hostname. Enable Third-Party Compatibility in Tapo if authentication fails.</p>
      <div class="automation-grid"><label>Tapo account email<input name="username" maxlength="256" autocomplete="off"></label>
      <label>Tapo password<input name="password" type="password" maxlength="1024" autocomplete="new-password"></label>
      <label>Check interval (seconds)<input name="pollSeconds" type="number" min="5" max="60" step="1" value="5" required></label></div>
      <label><input type="checkbox" name="clearPassword"> Remove saved password</label>
      <div class="tapo-account-actions"><button type="button" class="danger-secondary tapo-remove-account">Remove Tapo account</button><p>Removes the saved email and password from RTSPView and disables Tapo automations. Saved hubs and rules are kept. Your TP-Link account is not deleted.</p></div>
      <div class="tapo-hubs"></div><div class="control-buttons"><button type="button" class="secondary tapo-add-hub">Add hub manually</button>
      <button type="button" class="secondary tapo-scan">Discover hubs on network</button>
      <button type="button" class="secondary tapo-discover">Test connection & discover sensors</button></div>
      <div class="tapo-found-hubs"></div><p>Network discovery runs from the RTSPView computer. Hubs on other subnets or VLANs may require manual entry.</p>
      <p>Discovery tests the draft connection without saving or activating rules. Passwords are encrypted on this Windows account and omitted from configuration exports.</p>
      </details><div class="control-buttons"><button type="submit">Save Tapo automations</button><button type="button" class="secondary tapo-discard">Discard changes</button></div>
      <p class="tapo-message" role="status"></p>`;
    document.querySelector('#page-automation').append(form);
    form.addEventListener('input', mark); form.addEventListener('change', mark);
    q('.tapo-add-hub').onclick = () => { if (q('.tapo-hubs').children.length < 8) { addHub(); mark(); } };
    q('.tapo-add-rule').onclick = () => { if (q('.tapo-rules').children.length < 32) { addRule(); mark(); } };
    q('.tapo-discover').onclick = () => mutate(true);
    q('.tapo-scan').onclick = scanHubs;
    q('.tapo-remove-account').onclick = removeAccount;
    q('.tapo-discard').onclick = async () => { dirty = false; await load(config); };
    form.onsubmit = e => { e.preventDefault(); mutate(false); };
  }
  function addHub(hub = {id: id(), name: '', host: ''}) {
    const row = document.createElement('div'); row.className = 'tapo-hub automation-grid'; row.dataset.id = hub.id;
    row.append(input('Hub name', 'hub-name', hub.name), input('Hub address', 'hub-host', hub.host), button('Remove hub', () => { row.remove(); mark(); }));
    row.querySelector('.hub-name').addEventListener('input', updateSensorChoices);
    row.querySelector('.hub-name').maxLength = 100; row.querySelector('.hub-host').maxLength = 253;
    row.querySelectorAll('input').forEach(el => el.required = true);
    q('.tapo-hubs').append(row);
  }
  function sensorChoices() { return [['', 'Select a discovered T110 sensor'], ...sensors.map(s => [JSON.stringify([s.hubId, s.deviceId]), `${s.name} · ${hubName(s.hubId)}`])]; }
  function hubName(hubId) { return [...q('.tapo-hubs').children].find(h => h.dataset.id === hubId)?.querySelector('.hub-name').value || 'Hub unavailable'; }
  function updateSensorChoices() {
    for (const el of form.querySelectorAll('.sensor-choice')) {
      const value = el.value, choices = sensorChoices(); el.replaceChildren();
      choices.forEach(([v, text]) => el.add(new Option(text, v)));
      if (value && !choices.some(([v]) => v === value)) el.add(new Option('Saved sensor · currently unavailable', value));
      el.value = value;
    }
  }
  const actions = [[0, 'Restore normal behavior'], [1, 'Show overlay'], [2, 'Hide overlay'], [3, 'Activate standard layout'], [4, 'Activate automation layout']];
  function addRule(rule = {id: id(), name: 'Door sensor', enabled: true, match: 2, priority: 50, action: 1, clearAction: 0, unavailableAction: 0}) {
    const card = document.createElement('fieldset'); card.className = 'tapo-rule'; card.dataset.id = rule.id;
    const legend = document.createElement('legend'); legend.textContent = 'Door sensor rule'; card.append(legend);
    const grid = document.createElement('div'); grid.className = 'automation-grid';
    grid.append(input('Rule name', 'sensor-name', rule.name), input('Enable automation', 'sensor-enabled automation-toggle', rule.enabled, 'checkbox'));
    grid.querySelector('.sensor-name').required = true; grid.querySelector('.sensor-name').maxLength = 100;
    const toggle = grid.querySelector('.sensor-enabled'); toggle.setAttribute('role', 'switch');
    const toggleState = document.createElement('span'); toggleState.className = 'toggle-state'; toggleState.setAttribute('aria-hidden', 'true'); toggle.after(toggleState);
    card.dataset.priority = String(rule.priority ?? 50);
    const choice = select('Sensor', sensorChoices(), rule.deviceId ? JSON.stringify([rule.hubId, rule.deviceId]) : '');
    choice.querySelector('select').className = 'sensor-choice'; choice.querySelector('select').required = true;
    const state = select('While the door is', [[2, 'Open'], [1, 'Closed']], rule.match); state.querySelector('select').className = 'sensor-match';
    grid.append(choice, state);
    for (const [label, cls, value, choices] of [['Then', 'sensor-action', rule.action, actions], ['When the state clears', 'sensor-clear', rule.clearAction, actions], ['When unavailable', 'sensor-unavailable', rule.unavailableAction, actions.slice(0, 1).concat(actions.slice(2, 3))]]) {
      const el = select(label, choices, value ?? 0); el.querySelector('select').className = cls; grid.append(el);
    }
    const overlays = [config?.doorbellOverlay, config?.garageOverlay, ...(config?.additionalOverlays || [])].filter(o => o?.camera?.rtspUrl).map(o => [o.camera.slot, o.camera.name]);
    const overlay = select('Overlay', [['', 'Select overlay'], ...overlays], rule.overlaySlot || ''); overlay.className = 'sensor-overlay-field'; overlay.querySelector('select').className = 'sensor-overlay';
    const layouts = [['', 'Select saved layout'], ...(config?.layouts || []).map(l => [l.id, l.name])];
    const layout = select('Active layout', layouts, rule.layoutId || ''); layout.className = 'sensor-layout-field'; layout.querySelector('select').className = 'sensor-layout';
    const clearLayout = select('Layout after state clears', layouts, rule.clearLayoutId || ''); clearLayout.className = 'sensor-clear-layout-field'; clearLayout.querySelector('select').className = 'sensor-clear-layout';
    const cameras = (config ? layoutStreamInventory(config) : []).filter(c => c.enabled && c.rtspUrl).map(c => [c.slot, c.name]);
    const focus = select('Focus 1 camera', [['', 'Select focus camera'], ...cameras], rule.focusCameraSlot || ''); focus.className = 'sensor-focus-field'; focus.querySelector('select').className = 'sensor-focus';
    const second = select('Focus 2 camera', [[0, 'Leave second focus empty'], ...cameras], rule.secondFocusCameraSlot || 0); second.className = 'sensor-second-field'; second.querySelector('select').className = 'sensor-second';
    grid.append(overlay, layout, clearLayout, focus, second); card.append(grid);
    const buttons = document.createElement('div'); buttons.className = 'control-buttons';
    for (const [stateValue, label] of [[2, 'Test open'], [1, 'Test closed'], [0, 'Test unavailable']]) buttons.append(button(label, () => test(card, stateValue)));
    buttons.append(button('Delete rule', () => { card.remove(); mark(); })); card.append(buttons);
    const status = document.createElement('p'); status.className = 'sensor-rule-status'; status.setAttribute('role', 'status'); status.textContent = 'Save before testing. Tests affect the wall for 10 seconds.'; card.append(status);
    const targets = (preserveSelection = false) => {
      const action = Number(card.querySelector('.sensor-action').value), clear = Number(card.querySelector('.sensor-clear').value), unknown = Number(card.querySelector('.sensor-unavailable').value);
      overlay.hidden = ![action, clear, unknown].some(a => a === 1 || a === 2); overlay.querySelector('select').required = !overlay.hidden;
      for (const [field, a] of [[layout, action], [clearLayout, clear]]) {
        field.hidden = a !== 3 && a !== 4; const el = field.querySelector('select'); el.required = !field.hidden;
        if (el.dataset.kind !== String(a)) { const value = el.value; el.replaceChildren(new Option('Select layout', '')); (a === 4 ? config?.automationViewLayouts || [] : config?.layouts || []).forEach(l => el.add(new Option(l.name, l.id))); if (value && ![...el.options].some(o => o.value === value)) el.add(new Option('Unavailable selection', value)); el.value = value; el.dataset.kind = String(a); }
      }
      focus.hidden = action !== 4 && clear !== 4; focus.querySelector('select').required = !focus.hidden;
      second.hidden = ![[action, layout], [clear, clearLayout]].some(([a, f]) => a === 4 && config?.automationViewLayouts?.some(l => l.id === f.querySelector('select').value && l.focusSlots.length === 2));
      if (second.hidden && !preserveSelection) second.querySelector('select').value = '0';
    };
    card.refreshTargets = () => targets(true);
    card.addEventListener('change', () => targets()); targets(); q('.tapo-rules').append(card);
  }
  function draft(discovery = false) {
    const f = form.elements;
    return {settings: {enabled: f.enabled.checked, username: f.username.value.trim(), pollSeconds: Number(f.pollSeconds.value),
      hubs: [...q('.tapo-hubs').children].map(h => ({id: h.dataset.id, name: h.querySelector('.hub-name').value.trim(), host: h.querySelector('.hub-host').value.trim()})),
      rules: discovery ? [] : [...q('.tapo-rules').children].map(r => {
        const get = cls => r.querySelector('.sensor-' + cls).value;
        const [hubId, deviceId] = JSON.parse(get('choice') || '["",""]');
        return {id: r.dataset.id, name: get('name').trim(), enabled: r.querySelector('.sensor-enabled').checked, hubId, deviceId,
          match: Number(get('match')), priority: Number(r.dataset.priority), action: Number(get('action')), clearAction: Number(get('clear')),
          unavailableAction: Number(get('unavailable')), overlaySlot: Number(get('overlay')), layoutId: get('layout'), clearLayoutId: get('clear-layout'), focusCameraSlot: Number(get('focus')), secondFocusCameraSlot: Number(get('second'))};
      })}, password: f.password.value || null, clearPassword: f.clearPassword.checked};
  }
  function render(data) {
    const s = data.settings, f = form.elements;
    f.enabled.checked = s.enabled; f.username.value = s.username; f.password.value = ''; f.password.placeholder = data.hasPassword ? 'Saved — leave blank to keep' : 'Not set'; f.clearPassword.checked = false; f.pollSeconds.value = s.pollSeconds;
    q('.tapo-hubs').replaceChildren(); s.hubs.forEach(addHub);
    q('.tapo-rules').replaceChildren(); s.rules.forEach(addRule);
    q('.tapo-connection').open = !s.hubs.length; dirty = false; draftDiscovery = false; form.dataset.dirty = 'false'; loaded = true;
  }
  async function load(appConfig) {
    init(); if (appConfig) { config = {...appConfig}; updateTargets(appConfig); }
    try { if (!dirty && !busy) render(await api('/api/tapo')); await refresh(); }
    catch (e) { q('.tapo-message').textContent = e.message; }
  }
  function inventory(snapshot) {
    sensors = snapshot.sensors || []; updateSensorChoices();
    const area = q('.tapo-inventory'); area.replaceChildren();
    for (const h of snapshot.hubs || []) { const p = document.createElement('p'); p.textContent = `${h.name} ${h.model || ''}: ${h.message}`; area.append(p); }
    if (!sensors.length) { const p = document.createElement('p'); p.textContent = 'No T110 sensors reported. Add hubs and use Test connection & discover sensors.'; area.append(p); return; }
    const table = document.createElement('table'); const header = document.createElement('tr');
    ['Sensor', 'Hub', 'State'].forEach(name => { const cell = document.createElement('th'); cell.textContent = name; header.append(cell); });
    table.append(header);
    sensors.forEach(s => { const row = document.createElement('tr'); [s.name, hubName(s.hubId), ['Unavailable', 'Closed', 'Open'][s.state] || 'Unavailable'].forEach(text => { const cell = document.createElement('td'); cell.textContent = text; row.append(cell); }); table.append(row); }); area.append(table);
  }
  async function mutate(discovery) {
    if (busy || (!discovery && !form.reportValidity())) return;
    busy = true; const request = draft(discovery); form.querySelectorAll('button, input, select').forEach(b => b.disabled = true);
    q('.tapo-message').textContent = discovery ? 'Reading hubs…' : 'Saving…';
    try {
      const data = await api(discovery ? '/api/tapo/discover' : '/api/tapo', {method: discovery ? 'POST' : 'PUT', body: JSON.stringify(request)});
      if (discovery) { draftDiscovery = true; inventory(data); q('.tapo-message').textContent = `Found ${data.sensors.length} T110 sensor(s). Connection tested; nothing saved or activated.`; }
      else { render(data); q('.tapo-message').textContent = 'Tapo automations saved.'; }
    } catch (e) { q('.tapo-message').textContent = e.message; }
    finally { busy = false; form.querySelectorAll('button, input, select').forEach(b => b.disabled = false); }
  }
  async function removeAccount() {
    if (busy || !confirm('Remove the saved Tapo account from RTSPView and disable Tapo automations? Hubs and rules are kept. Unsaved Tapo edits will be discarded.')) return;
    busy = true; form.querySelectorAll('button, input, select').forEach(el => el.disabled = true);
    try {
      const data = await api('/api/tapo/account', {method: 'DELETE'});
      sensors = []; render(data); q('.tapo-found-hubs').replaceChildren(); inventory({hubs: [], sensors: []});
      q('.tapo-message').textContent = 'Tapo account removed from RTSPView. Automations are disabled; saved hubs and rules are kept.';
    } catch (e) { q('.tapo-message').textContent = e.message; }
    finally { busy = false; form.querySelectorAll('button, input, select').forEach(el => el.disabled = false); }
    await refresh();
  }
  async function scanHubs() {
    if (busy) return; busy = true; form.querySelectorAll('button, input, select').forEach(el => el.disabled = true);
    q('.tapo-message').textContent = 'Looking for hubs on the local network…';
    const area = q('.tapo-found-hubs'); area.replaceChildren();
    try {
      const result = await api('/api/tapo/discover-hubs', {method: 'POST', body: '{}'});
      for (const hub of result.discoveredHubs || []) {
        const row = document.createElement('div'); row.className = 'control-buttons tapo-found-hub'; const label = document.createElement('span'); label.textContent = `${hub.name} · ${hub.host}`;
        const add = button('Add discovered hub', () => { if ([...q('.tapo-hubs').children].some(h => h.querySelector('.hub-host').value.trim().toLowerCase() === hub.host.toLowerCase())) { q('.tapo-message').textContent = 'That address is already added.'; return; } if (q('.tapo-hubs').children.length >= 8) { q('.tapo-message').textContent = 'Up to eight hubs can be configured.'; return; } addHub(hub); mark(); row.remove(); });
        row.append(label, add); area.append(row);
      }
      q('.tapo-message').textContent = (result.discoveredHubs?.length || 0) + ' hub(s) found. Select hubs to add, then enter your account and discover sensors. Nothing saved yet.';
    } catch (e) { q('.tapo-message').textContent = e.message; }
    finally { busy = false; form.querySelectorAll('button, input, select').forEach(el => el.disabled = false); }
  }
  async function test(card, state) {
    const output = card.querySelector('.sensor-rule-status');
    if (dirty) { output.textContent = 'Save your changes before testing.'; return; }
    try { const result = await api(`/api/tapo/rules/${encodeURIComponent(card.dataset.id)}/test`, {method: 'POST', body: JSON.stringify({state})}); output.textContent = result.success ? 'Testing for 10 seconds. ' + result.message : result.message; }
    catch (e) { output.textContent = e.message; }
  }
  async function refresh() {
    if (!loaded || busy || refreshing || document.hidden) return; refreshing = true;
    try {
      const status = await api('/api/tapo/status');
      q('.tapo-status').textContent = `${status.connection} · ${status.message} Last checked: ${status.lastChecked ? new Date(status.lastChecked).toLocaleTimeString() : 'Not yet'}`;
      // Keep draft discovery available while editing; status must not remove its choices.
      if (!draftDiscovery) inventory(status);
      else {
        const merged = new Map(sensors.map(sensor => [JSON.stringify([sensor.hubId, sensor.deviceId]), sensor]));
        for (const sensor of status.sensors || []) merged.set(JSON.stringify([sensor.hubId, sensor.deviceId]), sensor);
        sensors = [...merged.values()]; updateSensorChoices();
      }
      for (const card of q('.tapo-rules').children) {
        const testing = status.testingRules.includes(card.dataset.id), active = status.activeRules.includes(card.dataset.id);
        if (!dirty) card.querySelector('.sensor-rule-status').textContent = testing ? 'Test active · restores sensor state after 10 seconds.' : active ? 'Rule condition active. Highest-priority action for each target wins.' : 'No override active. Tests affect the wall for 10 seconds.';
      }
    } catch (e) { q('.tapo-status').textContent = 'Sensor status unavailable. ' + e.message; }
    finally { refreshing = false; }
  }
  function updateTargets(appConfig) {
    if (!form || !config) return;
    Object.assign(config, appConfig);
    const overlays = [config.doorbellOverlay, config.garageOverlay, ...(config.additionalOverlays || [])].filter(o => o?.camera?.rtspUrl).map(o => [o.camera.slot, o.camera.name]);
    const cameras = layoutStreamInventory(config).filter(c => c.enabled && c.rtspUrl).map(c => [c.slot, c.name]);
    for (const [selector, items, label] of [['.sensor-overlay', overlays, 'Select overlay'], ['.sensor-focus', cameras, 'Select focus camera'], ['.sensor-second', [[0, 'Leave second focus empty'], ...cameras], 'Select focus camera']]) {
      for (const el of form.querySelectorAll(selector)) {
        const value = el.value; el.replaceChildren(new Option(label, ''));
        items.forEach(([v, text]) => el.add(new Option(text, String(v))));
        if (value && !items.some(([v]) => String(v) === value)) el.add(new Option('Unavailable selection', value));
        el.value = value;
      }
    }
    for (const card of q('.tapo-rules').children) {
      card.querySelectorAll('.sensor-layout, .sensor-clear-layout').forEach(el => delete el.dataset.kind);
      card.refreshTargets();
    }
  }
  function updateCamera(camera) {
    if (!config) return;
    const cameras = (config.cameras || []).slice(0, config.cameraCount || 9);
    const index = cameras.findIndex(c => c.slot === camera.slot);
    if (index < 0) cameras.push(camera); else cameras[index] = camera;
    updateTargets({cameras, cameraCount: cameras.length, deletedCameraSlots: (config.deletedCameraSlots || []).filter(slot => slot !== camera.slot)});
  }
  function updateOverlay(camera) {
    if (!config) return;
    const overlays = [config.doorbellOverlay, config.garageOverlay, ...(config.additionalOverlays || [])].filter(o => o && o.camera.slot !== camera.slot);
    overlays.push({camera}); updateTargets({doorbellOverlay: overlays.find(o => o.camera.slot === 10), garageOverlay: overlays.find(o => o.camera.slot === 11), additionalOverlays: overlays.filter(o => o.camera.slot !== 10 && o.camera.slot !== 11)});
  }
  return {init, load, refresh, updateTargets, updateCamera, updateOverlay};
})();
