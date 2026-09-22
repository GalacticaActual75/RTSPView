const displaySettingsUi = (() => {
  const form = document.querySelector('#displayForm'), select = form.elements.preferredMonitor;
  const identify = document.querySelector('#identifyDisplays'), message = document.querySelector('#monitorState');
  let savedDevice = '', signature = '';
  function load(config) {
    savedDevice = config.preferredMonitorDevice || ''; signature = '';
    select.replaceChildren(new Option(savedDevice || 'Display ' + (Number(config.preferredMonitor || 0) + 1) + ' — waiting for Viewer', config.preferredMonitor || 0));
    select.options[0].dataset.device = savedDevice;
  }
  function selectedDevice() { return select.selectedOptions[0]?.dataset.device || savedDevice; }
  function update(telemetry) {
    const displays = telemetry.viewerConnected ? telemetry.viewer?.displays || [] : [];
    identify.disabled = !displays.length;
    if (!displays.length) { message.textContent = 'Start Live View to discover or identify displays. The saved selection is retained.'; return; }
    const next = JSON.stringify(displays);
    if (next !== signature) {
      const device = selectedDevice(), index = select.value;
      const options = displays.map(display => { const option = new Option(display.label, display.index); option.dataset.device = display.deviceName; return option; });
      let chosen = options.find(o => device && o.dataset.device === device) || (!device && options.find(o => o.value === index));
      if (!chosen) { chosen = new Option((device || 'Display ' + (Number(index) + 1)) + ' — disconnected (fallback by index)', index); chosen.dataset.device = device; options.push(chosen); }
      select.replaceChildren(...options); chosen.selected = true; signature = next;
    }
    message.textContent = 'Saved by Windows display name, with the previous display index as a fallback when disconnected.';
  }
  identify.addEventListener('click', async () => {
    identify.disabled = true;
    try { const result = await api('/api/control/viewer/identify-displays', {method:'POST'}); message.textContent = result.message; }
    catch (error) { message.textContent = error.message; }
    finally { identify.disabled = false; }
  });
  return {load, update, selectedDevice};
})();
