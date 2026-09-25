const displaySettingsUi = (() => {
  const form = document.querySelector('#displayForm'), select = form.elements.preferredMonitor;
  const identify = document.querySelector('#identifyDisplays'), message = document.querySelector('#monitorState');
  let savedDevice = '', signature = '';
  const startupPanel=document.createElement('section');startupPanel.id='startupPanel';startupPanel.className='control-panel panel';
  startupPanel.innerHTML='<h2>Windows startup</h2><label><input id="liveViewStartup" type="checkbox" disabled> Open Live View when I sign in to Windows</label><p>This applies to the Windows account running RTSPView on the host. Sign-in is required; locking or unlocking Windows does not count as a new sign-in.</p><p id="startupState" role="status">Checking Windows startup…</p><div class="actions"><button id="refreshStartup" type="button" class="secondary">Refresh status</button><button id="applyStartup" type="button" disabled>Apply startup setting</button></div><p>Windows may ask for administrator approval on the host. Applying an enabled setting also repairs an older startup task. Stopping Live View keeps it stopped until you start it again or sign in next time.</p>';
  form.after(startupPanel);
  const startupToggle=startupPanel.querySelector('#liveViewStartup'),startupState=startupPanel.querySelector('#startupState'),startupApply=startupPanel.querySelector('#applyStartup'),startupRefresh=startupPanel.querySelector('#refreshStartup');
  function showStartup(status){startupToggle.checked=status.enabled;startupToggle.disabled=!status.managed;startupApply.disabled=!status.managed;startupState.textContent=status.message;}
  async function refreshStartup(){startupRefresh.disabled=true;startupApply.disabled=true;try{showStartup(await api('/api/startup'));}catch(error){startupToggle.disabled=true;startupState.textContent=error.message;}finally{startupRefresh.disabled=false;}}
  startupRefresh.onclick=refreshStartup;
  startupApply.onclick=async()=>{startupApply.disabled=true;startupRefresh.disabled=true;startupToggle.disabled=true;startupState.textContent='Waiting for Windows approval on the host…';try{showStartup(await api('/api/startup',{method:'PUT',body:JSON.stringify({enabled:startupToggle.checked})}));}catch(error){startupState.textContent=error.message;startupApply.disabled=false;startupToggle.disabled=false;}finally{startupRefresh.disabled=false;}};
  function load(config) {
    refreshStartup();
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
