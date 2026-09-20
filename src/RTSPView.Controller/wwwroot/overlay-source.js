const overlaySourceUi = (() => {
  const connectionFields = ['rtspUrl','transport','networkCacheMilliseconds','startupTimeoutSeconds','watchdogTimeoutSeconds','maximumReconnectBackoffSeconds','compositeStream','lowLatency','decodeAudio'];
  function bind(form, overlay, inventory) {
    const label = document.createElement('label'), select = document.createElement('select'), note = document.createElement('p');
    label.textContent = 'Video source'; select.name = 'sourceCameraSlot'; label.append(select);
    note.className = 'information-note';
    form.elements.rtspUrl.closest('label').parentElement.before(label, note);
    const own = Object.fromEntries(connectionFields.map(name => [name, read(form.elements[name])]));
    let selected = overlay.sourceCameraSlot || 0, sources = inventory;
    function read(input) { return input.type === 'checkbox' ? input.checked : input.value; }
    function write(input, value) { if(input.type === 'checkbox') input.checked = !!value; else input.value = value ?? ''; }
    function sync() {
      const id = Number(select.value), source = sources.find(c => c.slot === id && !c.overlaySourceSlot);
      for(const name of connectionFields) {
        const input = form.elements[name]; input.disabled = id !== 0;
        if (source) write(input, source[name]);
      }
      note.textContent = id ? 'Uses the saved connection from Streams. Source URL and connection changes follow automatically. Overlay visibility, shape, zoom, position and opacity remain independent. Playback may use another camera connection.' : 'Enter a separate RTSP URL, or choose an existing stream above. Overlay framing never changes the main tile.';
    }
    function refresh(next) {
      sources = next; const current = selected;
      select.replaceChildren(new Option('Own RTSP URL', '0'));
      for (const camera of sources.filter(c => !c.overlaySourceSlot && c.rtspUrl)) select.add(new Option(camera.name + ' · #' + camera.slot, camera.slot));
      if (current && ![...select.options].some(o => Number(o.value) === current)) select.add(new Option('Source unconfigured / unavailable · #' + current, current));
      select.value = String(current); sync();
    }
    select.addEventListener('change', () => {
      if (!selected) for(const name of connectionFields) own[name] = read(form.elements[name]);
      selected = Number(select.value);
      if (!selected) for(const name of connectionFields) write(form.elements[name], own[name]);
      sync(); form.dispatchEvent(new Event('input', {bubbles:true}));
    });
    // Discard restores form values without firing a change on every field.
    form.addEventListener('input', () => {
      const changed = Number(select.value) !== selected;
      selected = Number(select.value);
      if (changed || selected) sync();
    });
    form.refreshOverlaySources = refresh; refresh(inventory);
  }
  function refresh(inventory) { for(const form of document.querySelectorAll('.camera-card')) form.refreshOverlaySources?.(inventory); }
  return {bind, refresh};
})();
