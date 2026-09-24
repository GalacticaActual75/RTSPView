// Separate dialog: credentials and profile tokens are never camera form fields.
window.onvifUi = (() => {
  function attach(form) {
    const button = document.createElement('button');
    button.type = 'button'; button.className = 'secondary onvif-open'; button.textContent = 'Find ONVIF stream';
    form.elements.rtspUrl.closest('label').parentElement.after(button);
    const sync = () => { button.disabled = form.elements.rtspUrl.disabled; };
    form.addEventListener('change', sync); sync();
    button.onclick = () => { if (!form.elements.rtspUrl.disabled) open(form); };
  }
  function open(target) {
    const dialog = document.createElement('dialog'); dialog.className = 'onvif-dialog';
    dialog.setAttribute('aria-label', 'Find ONVIF stream');
    dialog.innerHTML = '<form><div class="actions"><h2>Find ONVIF stream</h2><button type="button" class="secondary" data-close>Close</button></div><p>Enable ONVIF in your camera first. Some cameras require a separate ONVIF account.</p><button type="button" class="secondary" data-discover>Find cameras on host network</button><label hidden>Discovered cameras<select data-devices><option value="">Choose a camera</option></select></label><label>Camera address<input name="address" placeholder="192.0.2.20 or http://camera:8000/onvif/device_service" required maxlength="2048" spellcheck="false"></label><div class="two"><label>ONVIF username<input name="username" autocomplete="off" maxlength="256"></label><label>ONVIF password<input name="password" type="password" autocomplete="off" maxlength="1024"></label></div><button type="submit" data-profiles>Load stream profiles</button><label hidden>Stream profile<select data-profiles-list></select></label><p data-status role="status" aria-live="polite">Discovery searches from the RTSPView host, not this browser. Enter an address manually if a camera is on another subnet.</p><p>The selected profile fills Source URL. Save &amp; apply to save its RTSP credentials, then assign the stream in Layouts to show it on the wall.</p><button type="button" data-use disabled>Use selected stream</button></form>';
    const form = dialog.querySelector('form'), status = dialog.querySelector('[data-status]');
    const devices = dialog.querySelector('[data-devices]'), profiles = dialog.querySelector('[data-profiles-list]');
    const use = dialog.querySelector('[data-use]'); let choices = [], busy = false;
    const abort = new AbortController();
    const credentials = () => Object.fromEntries(['address','username','password'].map(name => [name, form.elements[name].value]));
    function resetProfiles() { choices = []; profiles.replaceChildren(); profiles.parentElement.hidden = true; use.disabled = true; }
    function setBusy(value) {
      busy = value;
      for (const control of form.querySelectorAll('input,select,button:not([data-close])')) control.disabled = value;
      use.disabled = value || choices.length === 0;
    }
    async function request(path, body) {
      return api('/api/onvif/' + path, {method: 'POST', body: JSON.stringify(body), signal: abort.signal});
    }
    dialog.querySelector('[data-close]').onclick = () => dialog.close();
    dialog.addEventListener('close', () => { abort.abort(); form.reset(); dialog.remove(); }, {once: true});
    for (const input of form.querySelectorAll('input')) input.addEventListener('input', resetProfiles);
    devices.onchange = () => { if (devices.value) { form.elements.address.value = devices.value; resetProfiles(); } };
    dialog.querySelector('[data-discover]').onclick = async () => {
      if (busy) return; setBusy(true); status.textContent = 'Searching the host network for ONVIF cameras...';
      try {
        const result = await request('discover', {});
        devices.replaceChildren(new Option('Choose a camera', ''));
        for (const camera of result.devices) devices.add(new Option(camera.name + ' - ' + camera.address, camera.address));
        devices.parentElement.hidden = !result.devices.length;
        status.textContent = result.devices.length ? 'Choose a camera, enter its ONVIF credentials, and load its profiles.' : 'No cameras replied. Enable ONVIF and allow local discovery, or enter the camera address manually.';
      } catch (error) { if (!abort.signal.aborted) status.textContent = error.message; }
      finally { setBusy(false); }
    };
    form.onsubmit = async event => {
      event.preventDefault(); if (busy) return; resetProfiles(); setBusy(true); status.textContent = 'Connecting to camera and loading profiles...';
      try {
        const result = await request('profiles', credentials()); choices = result.profiles;
        for (const [index, profile] of choices.entries()) profiles.add(new Option([profile.name, profile.encoding, profile.width && profile.height ? profile.width + ' x ' + profile.height : ''].filter(Boolean).join(' - '), String(index)));
        profiles.parentElement.hidden = !choices.length;
        status.textContent = choices.length ? 'Choose a profile. A lower-resolution substream usually uses less bandwidth on a video wall.' : 'The camera returned no stream profiles.';
      } catch (error) { if (!abort.signal.aborted) status.textContent = error.message; }
      finally { setBusy(false); }
    };
    use.onclick = async () => {
      const profile = choices[Number(profiles.value)]; if (busy || !profile) return;
      setBusy(true); status.textContent = 'Getting the RTSP stream address...';
      try {
        const result = await request('stream', {...credentials(), profileToken: profile.token, mediaVersion: profile.mediaVersion});
        if (abort.signal.aborted || !target.isConnected) return;
        target.elements.rtspUrl.value = result.rtspUrl; target.elements.sourceMode.value = '1';
        target.elements.rtspUrl.dispatchEvent(new Event('input', {bubbles: true}));
        target.elements.sourceMode.dispatchEvent(new Event('change', {bubbles: true}));
        target.querySelector('.save-state').textContent = 'ONVIF stream selected. Save & apply, then assign it in Layouts.';
        dialog.close(); target.elements.rtspUrl.focus();
      } catch (error) { if (!abort.signal.aborted) status.textContent = error.message; }
      finally { setBusy(false); }
    };
    document.body.append(dialog); dialog.showModal(); form.elements.address.focus();
  }
  return {attach};
})();
