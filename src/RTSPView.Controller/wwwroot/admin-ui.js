/* Shared presentation helpers. Existing forms retain their controls and handlers. */
const adminUi = (() => {
  const streamPresentations = new WeakMap();
  const descriptions = {
    overview: 'Monitor the stream wall, stream health, and host performance.',
    cameras: 'Configure streams, connection settings, and recovery.',
    layouts: 'Arrange streams in a saved layout, then apply it to the wall.',
    overlays: 'Position, shape, and frame picture-in-picture streams. Apply changes to update the wall.',
    system: 'Configure RTSPView settings, networking, display behavior, updates, and backups.',
    automation: 'Choose what the wall shows when a camera detects a person.'
  };
  function collapseSections(root = document) {
    root.querySelectorAll('details[open]').forEach(details => { details.open = false; });
    root.querySelectorAll('.settings-info[aria-expanded="true"]').forEach(button => {
      button.setAttribute('aria-expanded', 'false');
      const content = document.getElementById(button.getAttribute('aria-controls'));
      if (content) content.hidden = true;
    });
  }
  function init() {
    // Popovers dismiss like menus; ordinary expandable settings remain open.
    const closeMenus = (target, restoreFocus = false) => {
      for (const menu of document.querySelectorAll('details.designer-menu[open]')) {
        if (target && menu.contains(target)) continue;
        menu.open = false;
        if (restoreFocus) menu.querySelector('summary')?.focus();
      }
    };
    document.addEventListener('pointerdown', event => closeMenus(event.target));
    document.addEventListener('focusin', event => closeMenus(event.target));
    document.addEventListener('keydown', event => {
      if (event.key === 'Escape' && document.querySelector('details.designer-menu[open]')) {
        closeMenus(null, true); event.preventDefault();
      }
    });
    document.addEventListener('invalid', event => {
      for(let p=event.target.parentElement;p;p=p.parentElement)if(p.tagName==='DETAILS')p.open=true;
    },true);
    const display = document.querySelector('#displayForm');
    display.querySelector('h2').textContent = 'Viewer / Wall Behavior';
    const help = {
      startFullScreen: 'Start the viewer in full screen.', hideMouseCursor: 'Hide the cursor after inactivity.',
      showTileBorders: 'Default for layouts that do not override borders in Canvas settings.', showCameraNames: 'Display names on the wall.', showCameraStats: 'Show stream and decoder details.',
      keepViewerAlwaysOnTop: 'Keep the viewer above other windows.'
    };
    for (const label of display.querySelectorAll('.display-toggle-grid label')) {
      const input = label.querySelector('input'), title = label.textContent.trim();
      for (const node of [...label.childNodes]) if (node.nodeType === Node.TEXT_NODE) node.remove();
      const text = document.createElement('span'); text.className = 'setting-copy';
      const name = document.createElement('span'); name.id = input.name + 'Label'; name.textContent = title;
      const hint = document.createElement('small'); hint.id = input.name + 'Help'; hint.textContent = help[input.name];
      input.setAttribute('aria-labelledby', name.id); input.setAttribute('aria-describedby', hint.id); text.append(name, hint); label.append(text);
    }
    document.querySelector('#displayState').setAttribute('role', 'status');
    const backup = document.querySelector('#configPanel');
    backup.querySelector('.control-buttons').classList.add('backup-actions');
    backup.querySelector('#importConfig').classList.add('danger-secondary');
    backup.querySelector('#configState').classList.add('information-note');
    document.querySelector('#updateState').setAttribute('role', 'status');
    for (const [selector,label,help] of [
      ['[data-action="restart-cameras"]','Restart all streams','Reconnect every stream without restarting the viewer application.'],
      ['[data-action="restart"]','Restart viewer application','Close and relaunch the viewer, including all streams.']
    ]) {
      const button = document.querySelector(selector), group = document.createElement('div'); group.className = 'restart-action';
      button.before(group); button.textContent = label;
      const description = document.createElement('small'); description.id = button.dataset.action + '-help'; description.textContent = help;
      button.setAttribute('aria-describedby',description.id); group.append(button,description);
    }
  }
  function page(id) { document.title = 'RTSPView · '+({overview:'Monitor',cameras:'Streams',layouts:'Layouts',overlays:'Overlays',automation:'Automation',system:'Settings'})[id]; }
  function host(status) {
    const target = document.querySelector('#host'); target.replaceChildren();
    for (const text of [status.hostname, 'v' + status.version]) {
      const item = document.createElement('span'); item.textContent = text; target.append(item);
    }
    // LAN addresses have their own actionable list on System; avoid repeating them there.
    const addresses = document.createElement('span'); addresses.className = 'host-addresses';
    addresses.textContent = status.lanAddresses.join(', '); target.append(addresses);
  }
  function stream(box, camera, viewerConnected) {
    const signature = JSON.stringify([camera?.state, camera?.frameWarning, camera?.fps, camera?.width, camera?.height,
      camera?.codec, camera?.bitrateKbps, camera?.decoder, camera?.reconnectCount, camera?.lastError, viewerConnected]);
    if (streamPresentations.get(box) === signature) return;
    streamPresentations.set(box, signature);
    const state = camera?.frameWarning ? 'Stale video' : camera?.state || (viewerConnected ? 'Telemetry unavailable' : 'Viewer offline');
    const tone = state === 'Live' ? 'healthy' : ['Disabled','NotConfigured'].includes(state) ? 'neutral' :
      ['Connecting','Buffering','Reconnecting','Stale video'].includes(state) ? 'warning' : 'error';
    box.dataset.tone = tone; box.querySelector('.state').textContent = ({Live:'Stream connected',StreamError:'Stream error',NotConfigured:'Not configured'})[state] || state;
    const details = box.querySelector('.live-details'); details.replaceChildren();
    if (!camera) { details.textContent = 'No current stream telemetry'; return; }
    const values = [`${camera.fps.toFixed(1)} fps`, camera.width && camera.height ? `${camera.width}×${camera.height}` : 'Resolution unknown',
      camera.codec || 'Codec unknown', `${camera.bitrateKbps.toFixed(0)} kb/s`, camera.decoder || 'Decoder unknown', `Reconnects: ${camera.reconnectCount}`];
    for (const value of values) { const item = document.createElement('span'); item.textContent = value; details.append(item); }
    if (camera.frameWarning || camera.lastError) {
      const error = document.createElement('span'); error.className = 'stream-error'; error.tabIndex = 0;
      error.textContent = [camera.frameWarning, camera.lastError].filter(Boolean).join(' · '); details.append(error);
    }
  }
  return {init, page, host, stream, collapseSections};
})();
