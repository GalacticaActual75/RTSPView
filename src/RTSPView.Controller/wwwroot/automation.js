/* All event processing runs in Controller. This tab only edits configuration. */
const automationUi = (() => {
  let form, rules, message, connection, inventory = [], overlays = [], loaded = false, dirty = false, busy = false, refreshing = false;
  const uuid = () => '10000000-1000-4000-8000-100000000000'.replace(/[018]/g, c =>
    (Number(c) ^ crypto.getRandomValues(new Uint8Array(1))[0] & 15 >> Number(c) / 4).toString(16));
  function init() {
    form = document.createElement('form'); form.id = 'automationForm'; form.className = 'panel control-panel';
    form.innerHTML = `<h2>Automation</h2>
      <p>When any selected camera detects a person, show an overlay until no person has been detected for your clear delay. Runs while the browser is closed.</p>
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
      <p>Select one or more source cameras and enter each camera’s Scrypted ObjectDetector topic. Any selected source can keep the overlay visible.</p>
      <div class="automation-rules"></div><button type="button" class="secondary automation-add">Add rule</button>
      <p>Configure overlay streams and appearance in Overlays. Leave an overlay disabled there if it should appear only during automation. An already enabled overlay remains visible after a rule clears.</p>
      <p>Clear means no new person detections; it does not prove the scene is empty. During a broker outage, the existing clear timer still expires.</p>
      </fieldset><div class="control-buttons"><button type="button" class="secondary automation-test">Test connection</button>
      <button type="button" class="secondary automation-cancel">Cancel edits</button><button type="submit">Save automation</button></div>
      <p class="automation-message" role="status"></p>`;
    rules = form.querySelector('.automation-rules'); message = form.querySelector('.automation-message'); connection = form.querySelector('.automation-connection');
    document.querySelector('#page-system').append(form);
    form.addEventListener('input', () => { dirty = true; }); form.addEventListener('change', () => { dirty = true; });
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
    const topicLabel = document.createElement('label'); topicLabel.textContent = 'MQTT person-event topic';
    const input = document.createElement('input'); input.className = 'source-topic'; input.required = true; input.maxLength = 512; input.placeholder = 'scrypted/<device-id>/ObjectDetector'; input.value = source.topic || ''; input.spellcheck = false; topicLabel.append(input);
    const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'secondary'; remove.textContent = 'Remove source'; remove.onclick = () => { row.remove(); dirty = true; };
    row.append(cameraLabel, topicLabel, remove); container.append(row);
  }
  function addRule(rule = {id: uuid(), name: 'Person overlay', enabled: true, clearMinutes: 2, sources: []}) {
    const card = document.createElement('fieldset'); card.className = 'automation-rule'; card.dataset.id = rule.id;
    card.innerHTML = `<legend>Person → overlay</legend><div class="automation-grid">
      <label>Rule name<input class="rule-name" maxlength="100" required></label>
      <label><input class="rule-enabled" type="checkbox"> Enabled</label>
      <label class="overlay-label">Show overlay</label>
      <label>Clear delay (minutes)<input class="rule-delay" type="number" min="0.1" max="120" step="0.1" required></label></div>
      <div class="rule-sources"></div><div class="control-buttons"><button type="button" class="secondary source-add">Add source camera</button>
      <button type="button" class="secondary rule-remove">Delete rule</button></div><p class="rule-status" role="status">Not saved</p>`;
    card.querySelector('.rule-name').value = rule.name; card.querySelector('.rule-enabled').checked = rule.enabled;
    card.querySelector('.rule-delay').value = rule.clearMinutes;
    const select = optionSelect(overlays, rule.overlaySlot, 'Overlay'); select.className = 'rule-overlay'; card.querySelector('.overlay-label').append(select);
    for (const source of rule.sources.length ? rule.sources : [{}]) addSource(card.querySelector('.rule-sources'), source);
    card.querySelector('.source-add').onclick = () => { if (card.querySelector('.rule-sources').children.length < 32) addSource(card.querySelector('.rule-sources')); dirty = true; };
    card.querySelector('.rule-remove').onclick = () => { card.remove(); dirty = true; };
    rules.append(card);
  }
  function draft() {
    const f = form.elements;
    return { settings: {enabled: f.enabled.checked, host: f.host.value.trim(), port: Number(f.port.value), tls: f.tls.value === 'true',
      authenticate: f.authenticate.value === 'true', username: f.username.value, clientId: f.clientId.value,
      rules: [...rules.children].map(card => ({id: card.dataset.id, name: card.querySelector('.rule-name').value,
        enabled: card.querySelector('.rule-enabled').checked, overlaySlot: Number(card.querySelector('.rule-overlay').value),
        clearMinutes: Number(card.querySelector('.rule-delay').value), sources: [...card.querySelectorAll('.automation-source')].map(row => ({
          cameraSlot: Number(row.querySelector('.source-camera').value), topic: row.querySelector('.source-topic').value.trim()}))}))},
      password: f.password.value || null, clearPassword: f.clearPassword.checked};
  }
  function render(data) {
    const settings = data.settings, f = form.elements;
    f.enabled.checked = settings.enabled; f.host.value = settings.host; f.port.value = settings.port;
    f.tls.value = String(settings.tls); f.authenticate.value = String(settings.authenticate); f.username.value = settings.username; f.clientId.value = settings.clientId;
    f.password.value = ''; f.password.placeholder = data.hasPassword ? 'Saved — leave blank to keep' : 'Not set'; f.clearPassword.checked = false;
    rules.replaceChildren(); settings.rules.forEach(addRule); dirty = false; loaded = true;
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
    if (busy || !form.reportValidity()) return;
    busy = true; const request = draft();
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
        card.querySelector('.rule-status').textContent = status.connection === 'Disabled' ? 'Automation disabled' : !state ? 'Not saved' : !state.enabled ? 'Disabled' : remaining ? `Detection active · clear in ${remaining}s` : 'Waiting for person';
      }
    } catch { connection.textContent = 'Controller unavailable'; }
    finally { refreshing = false; }
  }
  return {init, load, refresh};
})();
