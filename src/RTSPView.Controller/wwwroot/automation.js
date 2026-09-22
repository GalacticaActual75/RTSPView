/* All event processing runs in Controller. This tab only edits configuration. */
const automationUi = (() => {
  let form, rules, message, connection, inventory = [], overlays = [], loaded = false, dirty = false, busy = false, refreshing = false;
  let savedRules = [], discovered = [], diagnosticState = null, paused = false, clearedThrough = 0, diagnosticBusy = false;
  let viewLayouts = [], revision, savedEnabled = false;
  function mark() { dirty = true; form.dataset.dirty = "true"; automationPresentation.pending(form, true, savedEnabled); }
  const uuid = () => '10000000-1000-4000-8000-100000000000'.replace(/[018]/g, c =>
    (Number(c) ^ crypto.getRandomValues(new Uint8Array(1))[0] & 15 >> Number(c) / 4).toString(16));
  function init() {
    form = document.createElement('form'); form.id = 'automationForm'; form.className = 'panel control-panel';
    form.innerHTML = `<h2>MQTT automations</h2><label class="automation-master"><input name="enabled" type="checkbox" role="switch" class="automation-toggle"> Enable MQTT automations <span class="toggle-state" aria-hidden="true"></span></label>
      <p class="automation-connection" role="status">Loading connection status…</p>
      <fieldset class="automation-fields"><legend>MQTT connection</legend>
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
      </fieldset><fieldset class="automation-fields automation-rule-list"><legend>Your rules</legend>
      <p class="rules-intro">Open a rule to edit it, or add a new one.</p>
      <div class="automation-rules"></div><button type="button" class="secondary automation-add">Add rule</button>
      <details class="automation-help"><summary>How rules behave</summary><p>Choose <b>Automation only</b> as the target’s Display mode in Picture in picture to hide it while waiting. Always visible overlays remain on screen after a rule clears.</p>
      <p>Clear means no new person detections; it does not prove the scene is empty. During a broker outage, the existing clear timer still expires.</p>
      <p>Any detection-enabled stream uses your configured topic and zone mappings. Manual double-clicks override active automation; saved layouts are restored when focus clears.</p>
      </details></fieldset><details class="mqtt-tools automation-section" aria-label="MQTT discovery and details">
      <summary>Find camera events & troubleshoot</summary><p>Discover event topics using your connection settings. Listening lasts five minutes and does not activate rules.</p>
      <details><summary>Advanced discovery settings</summary><label>Topic prefix<input class="mqtt-prefix" value="scrypted" maxlength="400" spellcheck="false"></label></details>
      <div class="control-buttons"><button type="button" class="secondary mqtt-start">Discover topics / Start listening</button><button type="button" class="secondary mqtt-stop">Stop listening</button></div>
      <p class="mqtt-status" role="status">Not listening</p>
      <details class="mqtt-feed"><summary>Raw MQTT details</summary>
      <p>Received messages grouped by camera and topic. These are observations, not confirmation that a rule ran. Rule activity is shown above.</p>
      <div class="control-buttons"><button type="button" class="secondary mqtt-pause">Pause feed</button><button type="button" class="secondary mqtt-clear">Clear feed</button></div>
      <label>Filter camera or topic<input class="mqtt-filter" type="search" placeholder="Camera name or topic"></label>
      <div class="mqtt-messages"></div></details></details>
      <div class="control-buttons automation-savebar"><button type="button" class="secondary automation-test">Test connection</button>
      <p>Save &amp; apply saves MQTT connection settings and all rules in this panel, and activates enabled rules on this host.</p><button type="button" class="secondary automation-cancel">Discard changes</button><button type="submit">Save &amp; apply</button></div>
      <p class="automation-message" role="status"></p>`;
    rules = form.querySelector('.automation-rules'); message = form.querySelector('.automation-message'); connection = form.querySelector('.automation-connection');
    const broker = form.querySelector('.automation-fields');
    const brokerDetails = document.createElement('details'); brokerDetails.className = 'automation-section automation-broker';
    brokerDetails.innerHTML = '<summary>Connection settings <span class="broker-summary"></span></summary>';
    broker.before(brokerDetails); brokerDetails.append(broker);
    form.querySelector('.automation-rule-list').after(brokerDetails);
    broker.append(form.querySelector('.automation-test'));
    document.querySelector('#page-automation').append(form);
    automationPresentation.toggles(form);
    tapoUi.init(); automationTabs.init();
    const editLayouts=document.createElement('button');editLayouts.type='button';editLayouts.className='secondary automation-edit-layouts';editLayouts.textContent='Edit automation layouts';editLayouts.onclick=()=>wallDesigner.openAutomation();form.querySelector('.rules-intro').after(editLayouts);
    const markDirty = e => { if (!e.target.closest('.mqtt-tools')) { mark(); form.dataset.dirty = 'true'; } };
    form.addEventListener('input', markDirty); form.addEventListener('change', markDirty);
    form.querySelector('.mqtt-start').onclick = () => diagnostics(true);
    form.querySelector('.mqtt-stop').onclick = () => diagnostics(false);
    form.querySelector('.mqtt-pause').onclick = e => { paused = !paused; e.target.textContent = paused ? 'Resume feed' : 'Pause feed'; if (!paused) renderFeed(); };
    form.querySelector('.mqtt-clear').onclick = () => { clearedThrough = Math.max(clearedThrough, ...(diagnosticState?.messages || []).map(m => m.id)); form.querySelector('.mqtt-messages').replaceChildren(); };
    form.querySelector('.mqtt-filter').oninput = () => { if (!paused) renderFeed(); };
    form.querySelector('.mqtt-feed').ontoggle = () => { if (!paused) renderFeed(); };
    form.querySelector('.automation-add').onclick = () => { if (rules.children.length >= 32) return; addRule(); mark(); };
    form.querySelector('.automation-cancel').onclick = async () => { dirty = false; loaded = false; await load(); };
    form.querySelector('.automation-test').onclick = () => mutate(true);
    form.onsubmit = e => { e.preventDefault(); mutate(false); };
  }
  function optionSelect(items, value, label) {
    const select = document.createElement('select'); select.required = true; select.setAttribute('aria-label', label);
    const empty = document.createElement('option'); empty.value = ''; empty.textContent = `Select ${label.toLowerCase()}`; select.append(empty);
    for (const item of items) { const option = document.createElement('option'); option.value = item.slot; option.textContent = item.name + ' · #' + item.slot; select.append(option); }
    if (value && !items.some(i => i.slot === value)) { const missing = document.createElement('option'); missing.value = value; missing.textContent = `Unavailable stream (${value})`; select.append(missing); }
    select.value = value || ''; return select;
  }
  function addSource(container, source = {}) {
    const row = document.createElement('div'); row.className = 'automation-source automation-grid';
    const cameraLabel = document.createElement('label'); cameraLabel.textContent = 'Source camera';
    const select = optionSelect(inventory, source.cameraSlot, 'Source camera'); select.className = 'source-camera'; cameraLabel.append(select);
    const cameraColumn = document.createElement('div'); cameraColumn.append(cameraLabel);
    addCameraPreview(select, cameraColumn);
    const zoneLabel = document.createElement('label'); zoneLabel.textContent = 'Zone filter (optional)';
    const zone = document.createElement('input'); zone.className = 'source-zone'; zone.maxLength = 100; zone.placeholder = 'Any zone'; zone.value = source.requiredZone || ''; zone.spellcheck = false;
    const zoneHelp = document.createElement('small'); zoneHelp.textContent = 'Exact Scrypted object zone name, e.g. MQTT. Blank accepts people anywhere.';
    zoneLabel.append(zone, zoneHelp); cameraColumn.append(zoneLabel);
    const topicLabel = document.createElement('div'); topicLabel.className = 'source-event';
    const chooserLabel = document.createElement('label'); chooserLabel.textContent = 'Detected camera / event topic';
    const chooser = document.createElement('select'); chooser.className = 'source-discovered'; chooserLabel.append(chooser); topicLabel.append(chooserLabel);
    const advanced = document.createElement('details'); advanced.innerHTML = '<summary>Advanced: exact MQTT topic</summary>';
    const manualLabel = document.createElement('label'); manualLabel.textContent = 'MQTT person-event topic';
    const input = document.createElement('input'); input.className = 'source-topic'; input.required = true; input.maxLength = 512; input.placeholder = 'scrypted/<device-id>/ObjectDetector'; input.value = source.topic || ''; input.spellcheck = false; manualLabel.append(input); advanced.append(manualLabel); topicLabel.append(advanced);
    chooser.onchange = () => { input.value = chooser.value; advanced.open = !chooser.value; if (!chooser.value) input.focus(); mark(); };
    input.oninput = () => updateSourceChoices(row, false);
    select.onchange = () => { input.value = ''; zone.value = ''; updateSourceChoices(row, true); row.querySelector('.source-connection').open = !input.value; };
    const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'secondary'; remove.textContent = 'Remove source'; remove.onclick = () => { const card = row.closest('.automation-rule'); row.remove(); mark(); card.dispatchEvent(new Event('change', {bubbles:true})); };
    const eventDetails = document.createElement('details'); eventDetails.className = 'source-connection'; eventDetails.open = !source.topic;
    const eventSummary = document.createElement('summary'); eventSummary.textContent = 'Event connection'; eventDetails.append(eventSummary, topicLabel);
    const discover = document.createElement('button'); discover.type = 'button'; discover.className = 'secondary'; discover.textContent = 'Find camera events';
    discover.onclick = () => { const tools = form.querySelector('.mqtt-tools'); tools.open = true; tools.querySelector('.mqtt-start').focus(); };
    topicLabel.append(discover);
    row.append(cameraColumn, eventDetails, remove); container.append(row); updateSourceChoices(row, true);
  }
  function addRule(rule = {id: uuid(), name: 'Person overlay', enabled: true, clearMinutes: 2, sources: []}, collapsed = false) {
    const card = document.createElement('fieldset'); card.className = 'automation-rule'; card.dataset.id = rule.id;
    card.innerHTML = `<div class="automation-grid rule-identity">
      <label>Rule name<input class="rule-name" maxlength="100" required></label>

      <label><input class="rule-enabled automation-toggle" type="checkbox" role="switch"> Enable automation <span class="toggle-state" aria-hidden="true"></span></label></div>
      <h4>When a person is detected on</h4>
      <label>Trigger streams<select class="rule-source-mode"><option value="selected">Selected streams</option><option value="any">Any detection-enabled stream</option></select></label>
      <p class="source-mode-help" hidden>Uses the stream/topic/zone mappings configured across your rules, including disabled rules. Add mappings with Selected streams first. RTSP feeds without detection mappings cannot trigger this rule.</p><div class="rule-sources"></div>
      <button type="button" class="secondary source-add">Add source camera</button>
      <h4>Then</h4><div class="automation-grid rule-action-grid">
      <label>Action<select class="rule-action"><option value="0">Show overlay</option><option value="1">Fullscreen camera</option><option value="2">Automation layout</option></select></label>
      <label class="target-label"></label></div>
      <p class="target-help"></p>
      <h4>Keep it shown until</h4>
      <label>When another stream detects a person<select class="rule-takeover"><option value="hold">Hold until clear delay expires</option><option value="newer">Allow a newer detection to take over</option></select></label>
      <p>This setting controls takeovers between rules of equal priority, as ordered in the Priority tab. Repeated detections extend the timer without stealing focus. After a view clears, another still-active rule may resume.</p><label class="rule-timing">No new person detections for (minutes)<input class="rule-delay" type="number" min="0.1" max="120" step="0.1" required></label>
      <div class="control-buttons rule-actions">
      <button type="button" class="secondary rule-remove">Delete rule</button></div><p class="rule-status" role="status">Not saved</p>`;
    const editor = document.createElement('details'); editor.className = 'rule-editor'; editor.open = !collapsed;
    const summary = document.createElement('summary'); summary.className = 'rule-summary'; editor.append(summary);
    for (const child of [...card.children]) if (child.tagName !== 'LEGEND' && !child.classList.contains('rule-status')) editor.append(child);
    card.querySelector('.rule-status').before(editor);
    if (collapsed) card.querySelector('.rule-status').textContent = 'Saved';
    card.querySelector('.rule-name').value = rule.name; card.querySelector('.rule-enabled').checked = rule.enabled;
    card.querySelector('.rule-delay').value = rule.clearMinutes;
    card.dataset.priority = String(rule.priority ?? 50);
    card.querySelector('.rule-takeover').value=rule.allowNewerDetection?'newer':'hold';
    card.querySelector('.rule-source-mode').value=rule.anyConfiguredSource?'any':'selected';
    card.dataset.overlayTarget = rule.overlaySlot || ''; card.dataset.cameraTarget = rule.cameraSlot || 0;
    card.dataset.layoutTarget = rule.layoutId || '';card.dataset.secondCameraTarget=rule.secondCameraSlot||0;
    card.querySelector('.rule-action').value = rule.action || 0;
    renderActionTarget(card);
    card.querySelector('.rule-action').onchange = () => { renderActionTarget(card); mark(); };
    for (const source of rule.sources.length ? rule.sources : [{}]) addSource(card.querySelector('.rule-sources'), source);
    card.querySelector('.source-add').onclick = () => { if (card.querySelector('.rule-sources').children.length < 32) addSource(card.querySelector('.rule-sources')); mark(); card.dispatchEvent(new Event('change', {bubbles:true})); };
    const applySourceMode=()=>{
      const any=card.querySelector('.rule-source-mode').value==='any';
      card.querySelector('.rule-sources').hidden=any;card.querySelector('.source-add').hidden=any;
      card.querySelector('.source-mode-help').hidden=!any;
      for(const input of card.querySelectorAll('.rule-sources input,.rule-sources select'))input.disabled=any;
    };
    card.querySelector('.rule-source-mode').onchange=()=>{applySourceMode();mark();};applySourceMode();
    card.querySelector('.rule-remove').onclick = () => { card.remove(); mark(); };
    const updateSummary = () => {
      const action = card.querySelector('.rule-action'), target = card.querySelector('.rule-target');
      const cameras = [...card.querySelectorAll('.source-camera')].map(s => s.selectedOptions[0]?.textContent).filter(Boolean);
      const layout = card.querySelector('.rule-layout')?.selectedOptions[0]?.textContent;
      const second = card.querySelector('.rule-second-target');
      automationPresentation.summary(summary, card.querySelector('.rule-name').value, [
        ['Trigger', 'Person · ' + (card.querySelector('.rule-source-mode').value === 'any' ? 'Any mapped stream' : cameras.join(', ') || 'Select source')],
        ['Action', action.selectedOptions[0].textContent + (layout ? ' · ' + layout : '')],
        ['Target', (target.selectedOptions[0]?.textContent || 'Select target') + (Number(second?.value) > 0 ? ' + ' + second.selectedOptions[0].textContent : '')],
        ['Duration', card.querySelector('.rule-delay').value + ' min without detections → resume other rules / saved wall']
      ]);

    };
    const takeover = card.querySelector('.rule-takeover').closest('label'), help = takeover.nextElementSibling;
    const advanced = document.createElement('details'); advanced.className = 'rule-advanced'; advanced.innerHTML = '<summary>Advanced · equal-priority behavior</summary>';
    card.querySelector('.rule-timing').after(advanced); advanced.append(takeover, help);
    card.addEventListener('input', updateSummary); card.addEventListener('change', updateSummary); updateSummary();
    const testControls = document.createElement('div'); testControls.className = 'control-buttons rule-test-controls';
    const testSources=rule.anyConfiguredSource?[...new Map(savedRules.flatMap(r=>r.sources).map(s=>[s.cameraSlot,s])).values()]:rule.sources;
    const testSource = optionSelect(testSources.map(s => ({slot:s.cameraSlot, name:inventory.find(c => c.slot === s.cameraSlot)?.name || 'Camera'})), testSources[0]?.cameraSlot, 'Test source camera');
    testSource.className = 'rule-test-source'; testSource.required = false;
    testSource.hidden = testSources.length < 2;
    const testButton = document.createElement('button'); testButton.type = 'button'; testButton.className = 'secondary'; testButton.textContent = 'Test';
    testButton.setAttribute('aria-label', 'Test ' + rule.name);
    const testMessage = document.createElement('p'); testMessage.className = 'rule-test-message'; testMessage.setAttribute('role','status');
    // Test controls do not edit the saved rule or mark the form dirty.
    testSource.addEventListener('input', e => e.stopPropagation()); testSource.addEventListener('change', e => e.stopPropagation());
    testButton.onclick = async () => {
      if (dirty || !savedRules.some(r => r.id === rule.id)) { testMessage.textContent = 'Save & apply before testing this rule.'; return; }
      if (!form.elements.enabled.checked || !rule.enabled) { testMessage.textContent = 'Enable automation and this rule, then save changes before testing.'; return; }
      testButton.disabled = true; testMessage.textContent = 'Simulating a person detection…';
      try {
        const result = await api('/api/automation/rules/' + encodeURIComponent(rule.id) + '/test', {method:'POST', body:JSON.stringify({sourceSlot:Number(testSource.value)})});
        automationPresentation.status(testMessage, result.message, result.success ? 'healthy' : 'error');
        await refresh();
      } catch (e) { testMessage.textContent = e.message; }
      finally { testButton.disabled = false; }
    };
    testControls.append(testSource, testButton); card.append(testControls, testMessage);
    automationPresentation.toggles(card);
    rules.append(card);
    if (!collapsed) card.querySelector(".rule-name").focus();
  }
  function renderActionTarget(card) {
    const action = Number(card.querySelector('.rule-action').value), label = card.querySelector('.target-label');
    const actionGrid=card.querySelector('.rule-action-grid');
    actionGrid.append(label);
    card.querySelector('.rule-layout-label')?.remove();
    const items = action === 0 ? overlays : inventory.filter(c =>
      (c.enabled && !overlays.some(o => o.slot === c.slot)) || (action === 1 && overlays.some(o => o.slot === c.slot)));
    const selected = action === 0 ? Number(card.dataset.overlayTarget) : Number(card.dataset.cameraTarget);
    const select = optionSelect(items, selected, action === 0 ? 'Overlay' : 'Camera to focus'); select.className = 'rule-target';
    if (action !== 0) { select.add(new Option('Camera that detected the person', '0'), 1); select.value = String(selected); }
    label.textContent = action === 0 ? 'Show overlay' : 'Camera to focus'; label.append(select);
    select.onchange = () => { card.dataset[action === 0 ? 'overlayTarget' : 'cameraTarget'] = select.value; };
    card.querySelector('.target-help').textContent = action === 0 ? 'Any selected source can show this overlay until all sources have been clear for the delay.' : action === 1 ? 'Fill the viewer with this camera, then restore the previous layout. Each triggering camera has its own clear timer when following detections.' : 'Automatic layout: this camera occupies a 2×2 tile at the upper left, with all other enabled, configured main streams in smaller tiles. Uses the active layout’s orientation and restores that layout after the clear delay. Tile sizes and positions cannot currently be customized. Save & apply, then use Test to preview it.';
    card.querySelector('.rule-layout-choice')?.remove();
    if(action===2){
      const block=document.createElement('div');block.className='rule-layout-choice automation-grid';const layoutLabel=document.createElement('label');layoutLabel.className='rule-layout-label';layoutLabel.textContent='Automation layout';const picker=document.createElement('select');picker.className='rule-layout';picker.add(new Option('Automatic — all enabled streams',''));for(const item of viewLayouts)picker.add(new Option(item.name,item.id));if(card.dataset.layoutTarget&&!viewLayouts.some(l=>l.id===card.dataset.layoutTarget))picker.add(new Option('Unavailable layout',card.dataset.layoutTarget));picker.value=card.dataset.layoutTarget;picker.onchange=()=>{card.dataset.layoutTarget=picker.value;renderActionTarget(card);};layoutLabel.append(picker);actionGrid.append(layoutLabel);block.append(label);
      const dual=viewLayouts.find(l=>l.id===card.dataset.layoutTarget)?.focusSlots?.length===2;
      if(dual){label.firstChild.textContent='Focus 1 camera';select.setAttribute('aria-label','Focus 1 camera');const secondLabel=document.createElement('label');secondLabel.textContent='Focus 2 camera';const second=optionSelect(items,Number(card.dataset.secondCameraTarget),'Focus 2 camera');second.className='rule-second-target';second.add(new Option('Next camera with an active detection','0'),1);second.value=String(card.dataset.secondCameraTarget||0);second.onchange=()=>{card.dataset.secondCameraTarget=second.value;};secondLabel.append(second);block.append(secondLabel);}
      const edit=document.createElement('button');edit.type='button';edit.className='secondary';edit.textContent='Edit automation layouts';edit.style.gridColumn='1 / -1';edit.style.justifySelf='start';edit.onclick=()=>wallDesigner.openAutomation();block.append(edit);card.querySelector('.target-help').before(block);
      card.querySelector('.target-help').textContent='Choose a saved one-focus or two-focus layout, or use the automatic arrangement. This rule supplies the camera for the focus tile; the layout editor only sets its position and size. For a two-focus layout, choose a Focus 2 camera to show both cameras on the same trigger, or let the next active detection fill it. Choose “Camera that detected the person” to focus multiple source cameras. The standard view returns when detections clear.';
    }
  }
  function draft() {
    const f = form.elements;
    return { settings: {enabled: f.enabled.checked, host: f.host.value.trim(), port: Number(f.port.value), tls: f.tls.value === 'true',
      authenticate: f.authenticate.value === 'true', username: f.username.value, clientId: f.clientId.value,
      rules: [...rules.children].map(card => ({id: card.dataset.id, name: card.querySelector('.rule-name').value,
        anyConfiguredSource:card.querySelector('.rule-source-mode').value==='any', allowNewerDetection:card.querySelector('.rule-takeover').value==='newer',
        enabled: card.querySelector('.rule-enabled').checked, action: Number(card.querySelector('.rule-action').value),
        layoutId: card.dataset.layoutTarget || '',
        secondCameraSlot: Number(card.querySelector('.rule-second-target')?.value||0),
        overlaySlot: Number(card.querySelector('.rule-action').value) === 0 ? Number(card.querySelector('.rule-target').value) : 0,
        cameraSlot: Number(card.querySelector('.rule-action').value) !== 0 ? Number(card.querySelector('.rule-target').value) : 0,
        priority: Number(card.dataset.priority), clearMinutes: Number(card.querySelector('.rule-delay').value), sources: [...card.querySelectorAll('.automation-source')].map(row => ({
          cameraSlot: Number(row.querySelector('.source-camera').value), topic: row.querySelector('.source-topic').value.trim(), requiredZone: row.querySelector('.source-zone').value.trim()})).filter(s=>card.querySelector('.rule-source-mode').value!=='any'||s.topic)}))},
      password: f.password.value || null, clearPassword: f.clearPassword.checked, revision};
  }
  function render(data) {
    const settings = data.settings, f = form.elements; revision = data.revision; savedEnabled = settings.enabled;
    f.enabled.checked = settings.enabled; f.host.value = settings.host; f.port.value = settings.port;
    f.tls.value = String(settings.tls); f.authenticate.value = String(settings.authenticate); f.username.value = settings.username; f.clientId.value = settings.clientId;
    f.password.value = ''; f.password.placeholder = data.hasPassword ? 'Saved — leave blank to keep' : 'Not set'; f.clearPassword.checked = false;
    form.querySelector('.broker-summary').textContent = settings.host ? `${settings.host}:${settings.port}` : 'Set up MQTT';
    form.querySelector('.automation-broker').open = !settings.host;
    savedRules = settings.rules;
    rules.replaceChildren(); settings.rules.forEach(rule => addRule(rule, true)); dirty = false; form.dataset.dirty = 'false'; loaded = true; refreshOverlayLinks(); automationPresentation.pending(form, false, savedEnabled);
    form.querySelector('.rules-intro').textContent = settings.rules.length ? settings.rules.length + ' saved rule(s). Expand a rule to edit.' : 'No rules yet. Configure MQTT, add a stream in Streams, then connect its person events to a rule.';
  }
  async function load(config) {
    await tapoUi.load(config);
    try {
      if (config) { viewLayouts = config.automationViewLayouts || []; overlays = [config.doorbellOverlay, config.garageOverlay, ...(config.additionalOverlays || [])].map(o => o.camera).filter(c => c.rtspUrl);
        inventory = [...layoutStreamInventory(config), ...overlays].filter(c => c.rtspUrl); }
      if (!dirty && !busy) render(await api('/api/automation'));
      else if (config) refreshStreamChoices();
      await refresh();
    } catch (e) { message.textContent = e.message; }
  }
  async function mutate(test) {
    if (busy) return;
    // Testing a connection must also work while a new rule is incomplete.
    if (!test && !form.reportValidity()) return;
    busy = true; const request = draft();
    if (test) request.settings.rules = dirty ? [] : savedRules;
    form.inert = true;
    for (const button of form.querySelectorAll('button')) button.disabled = true;
    for (const fieldset of form.querySelectorAll('fieldset')) fieldset.disabled = true;
    message.textContent = test ? 'Testing from Controller…' : 'Saving…';
    try {
      const result = await api(test ? '/api/automation/test' : '/api/automation', {method: test ? 'POST' : 'PUT', body: JSON.stringify(request)});
      if (test) automationPresentation.status(message, result.message + (dirty ? ' Draft rule subscriptions were not checked.' : ''), result.success ? 'healthy' : 'error');
      else { render(result); message.textContent = 'Applied — controller updating automation.'; }
    } catch (e) { message.textContent = e.message; }
    finally { busy = false; form.inert = false; for (const button of form.querySelectorAll('button')) button.disabled = false; for (const fieldset of form.querySelectorAll('fieldset')) fieldset.disabled = false; }
  }
  async function refresh() {
    tapoUi.refresh();
    if (!loaded || refreshing) return; refreshing = true;
    try {
      const status = await api('/api/automation/status');
      const {presentation} = await api('/api/automation/presentation');
      automationPresentation.activity(form, status);
      const problem = status.configurationError || (status.delivery?.success === false && savedEnabled ? automationPresentation.delivery(status.delivery) + ' Check Live View in Quick actions.' : '');
      automationPresentation.status(connection, 'MQTT: ' + status.connection + (problem ? ' · ' + problem : ' · ' + (status.connection==='Disabled'?'Integration disabled · enable MQTT and Save & apply to activate rules':status.lastResult || 'Waiting for person events')), problem || status.connection === 'Error' ? 'error' : status.connection === 'Connected' ? 'healthy' : 'neutral');
      connection.title = status.lastResult || '';
      for (const card of rules.children) {
        const state = status.rules.find(r => r.id === card.dataset.id), remaining = state?.expiresAt ? Math.max(0, Math.ceil((new Date(state.expiresAt) - Date.now()) / 1000)) : 0;
        const condition = state?.error ? 'Invalid rule · ' + state.error : !savedEnabled ? 'Automation disabled' : !state ? 'Not saved / status pending' : !state.enabled ? 'Rule disabled' : remaining ? 'Detection active · expires in ' + remaining + 's' : 'Waiting for detection';
        const receipt = state?.lastEvent ? ' · Last event ' + new Date(state.lastEvent).toLocaleTimeString() : '';
        automationPresentation.status(card.querySelector('.rule-status'), condition + receipt + (savedEnabled && state?.enabled ? ' · ' + automationPresentation.delivery(state.delivery) + ' · ' + automationPresentation.visibility(presentation, 'MQTT', card.dataset.id) : ''), state?.error || state?.delivery?.success === false ? 'error' : 'neutral');
      }
      if (!document.querySelector('#page-automation').hidden) await refreshDiagnostics();
    } catch {
      automationPresentation.status(connection, 'RTSPView service unreachable. Status cannot be refreshed; check the RTSPView host.', 'error');
      for (const card of rules.children) automationPresentation.status(card.querySelector('.rule-status'), 'Status unavailable · previous activity is not confirmed', 'warning');
    }
    finally { refreshing = false; }
  }
  function updateSourceChoices(row, suggest) {
    const input = row.querySelector('.source-topic'), chooser = row.querySelector('.source-discovered');
    const slot = Number(row.querySelector('.source-camera').value), camera = inventory.find(c => c.slot === slot);
    if (suggest && !input.value && camera) {
      const known = [...new Set(savedRules.flatMap(r => r.sources).filter(s => s.cameraSlot === slot).map(s => s.topic))];
      const matches = discovered.filter(t => t.cameraName?.toLowerCase() === camera.name.toLowerCase());
      if (known.length === 1 || camera.scryptedTopic || matches.length === 1) { input.value = known.length === 1 ? known[0] : camera.scryptedTopic || matches[0].topic; mark(); form.dataset.dirty = 'true'; }
    }
    const choices = discovered.map(t => ({value: t.topic, label: `${t.cameraName || 'Camera name unavailable'} · ${t.topic}${t.personSeen ? ' · Person observed' : ''}`}));
    if (input.value && !choices.some(c => c.value === input.value)) choices.unshift({value:input.value, label:'Configured topic (not seen in this discovery session) · ' + input.value});
    const signature = JSON.stringify(choices);
    if (chooser.dataset.choices !== signature) {
      chooser.replaceChildren(new Option('Choose a discovered topic, or enter one under Advanced', ''));
      for (const choice of choices) chooser.add(new Option(choice.label, choice.value));
      chooser.dataset.choices = signature;
    }
    chooser.value = input.value;
    const summary = row.querySelector('.source-connection > summary');
    if (summary) summary.textContent = input.value ? 'Event configured · Edit' : 'Connect camera event';
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
      if (!select.value) { image.removeAttribute('src'); delete image.dataset.snapshotSlot; return; }
      const name = inventory.find(c => c.slot === Number(select.value))?.name || 'Selected camera';
      image.alt = name + ' latest snapshot'; refresh.setAttribute('aria-label', 'Refresh snapshot for ' + name);
      image._ageLabel = caption;
      image.onload = () => { image.hidden = false; };
      image.onerror = () => { image.hidden = true; caption.textContent = 'Snapshot unavailable'; };
      dashboardUX.snapshot(image,Number(select.value));
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
      info.querySelector('.overlay-mode-help').textContent = overlayForm.elements.enabled.value === 'false' ? 'Automation only: the feed stays connected in the background, hidden until a rule detects a person. Save & apply in Picture in picture to use this mode.' : 'Always visible: stays on screen even when automation is idle.';
      const linked = savedRules.filter(r => (r.action || 0) === 0 && r.overlaySlot === slot);
      info.querySelector('.overlay-rule-links').textContent = linked.length ? 'Linked rules: ' + linked.map(r => r.name + (r.enabled ? '' : ' (disabled)')).join(', ') : 'No automation rules assigned';
    }
  }
  function updateOverlay(camera) {
    tapoUi.updateOverlay(camera);
    overlays = overlays.filter(c => c.slot !== camera.slot); if (camera.rtspUrl) overlays.push(camera);
    inventory = inventory.filter(c => c.slot !== camera.slot && c.slot !== camera.slot+23); if (camera.rtspUrl) inventory.push(camera, {...camera,slot:camera.slot+23,name:camera.name+' (overlay source)',enabled:true});
    refreshStreamChoices(); refreshOverlayLinks(); refresh();
  }
  function refreshStreamChoices() {
    for(const card of rules.children) {
      for(const select of card.querySelectorAll('.source-camera')) {
        const value=select.value, replacement=optionSelect(inventory,Number(value),'Source camera');
        select.replaceChildren(...replacement.options);select.value=value;
      }
      const test = card.querySelector('.rule-test-source');
      if (test) for (const option of test.options) {
        const camera = inventory.find(c => String(c.slot) === option.value);
        if (option.value) option.textContent = camera ? camera.name + ' · #' + camera.slot : 'Unavailable stream (' + option.value + ')';
      }
      renderActionTarget(card);
    }
  }
  function updateCamera(camera) {
    tapoUi.updateCamera(camera);
    inventory=inventory.filter(c=>c.slot!==camera.slot);
    if(camera.rtspUrl)inventory.push(camera);
    refreshStreamChoices();
  }
  async function refreshLayouts(){viewLayouts=(await api('/api/automation/layouts')).layouts;for(const card of rules.children)renderActionTarget(card);}
  return {init, load, refresh, decorateOverlay, updateCamera, updateOverlay, refreshOverlayLinks, refreshLayouts};
})();
