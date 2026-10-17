const patchUpdates = (() => {
  let toggle, state, installedVersion, availableVersion, history, check, install, latest, busy = false, loading = false, installing = false, lastRefresh = 0, revision = 0;
  function init() {
    const section = document.createElement('section');
    section.className = 'patch-updates';
    section.setAttribute('aria-labelledby','patchUpdatesTitle');
    section.innerHTML = `<div class="panel-head"><div><h3 id="patchUpdatesTitle">Patches</h3>
      <p>Smaller dashboard and application fixes for your installed release.</p></div>
      <div class="control-buttons"><button type="button" class="secondary patch-check">Check for new patches</button><button type="button" class="patch-install" disabled>Install patch</button></div></div>
      <dl class="patch-versions"><div><dt>Installed version</dt><dd class="patch-installed">Loading…</dd></div><div><dt>Available patch</dt><dd class="patch-available">Loading…</dd></div></dl>
      <p class="patch-state" role="status" aria-live="polite">Loading patch settings…</p><p class="patch-history"></p>
      <label class="toggle-control"><input type="checkbox" role="switch" disabled><span class="toggle-track" aria-hidden="true"></span>Automatically install patches during scheduled restarts</label>
      <p>Off by default. When enabled, patches install during the Live View restart schedule in Maintenance and restart the whole application. Automatic installation requires a current, enabled maintenance helper and a restart schedule.</p>
      <p>With automatic installation off, daily checks and manual installation remain available. Turning it off cancels a pending automatic installation unless the installer has already started.</p>`;
    document.querySelector('#updatePanel').append(section);
    toggle = section.querySelector('input'); state = section.querySelector('.patch-state');
    installedVersion = section.querySelector('.patch-installed'); availableVersion = section.querySelector('.patch-available');
    install = section.querySelector('.patch-install'); check = section.querySelector('.patch-check');
    history = section.querySelector('.patch-history');
    check.onclick = () => refresh(true);
    toggle.onchange = async () => {
      busy = true; revision++; syncControls();
      try { render(await api('/api/patch-update/automatic', {method:'PUT', body:JSON.stringify({enabled:toggle.checked})})); }
      catch (error) { toggle.checked = latest?.autoInstall ?? false; state.textContent = error.message; }
      finally { busy = false; syncControls(); }
    };
    install.onclick = async () => {
      const version = latest?.availableVersion;if (!version || busy || loading || installing) return;
      busy = true; revision++; syncControls();
      try {
        if (!await uiDialogs.ask('Install patch '+version+' now? The stream wall and dashboard will briefly disconnect while the application restarts.',{accept:'Install patch'})) return;
        state.textContent = 'Preparing and verifying patch…';
        const result = await api('/api/update/install', {method:'POST', body:JSON.stringify({confirmed:true,channel:version.includes('-beta.')?'beta':'stable',version})});
        state.textContent = result.message; installing = true;
      } catch (error) { state.textContent = error.message; }
      finally { busy = false; syncControls(); }
    };
  }
  function syncControls() {
    check.disabled = busy || loading || installing;check.textContent = loading ? 'Checking patches…' : 'Check for new patches';
    install.disabled = busy || loading || installing || !latest?.availableVersion;
    install.textContent = installing ? 'Installation started' : 'Install patch';
    toggle.disabled = busy || loading || installing || !latest;
  }
  function render(status) {
    latest = status; toggle.checked = !!status.autoInstall;
    const describe = version => version?.replace(/-patch\.(\d+)$/, ' · patch $1') || 'None';
    installedVersion.textContent = describe(status.installedVersion);
    availableVersion.textContent = describe(status.availableVersion);
    state.textContent = status.message;
    const date = value => value ? new Date(value).toLocaleString() : 'Pending';
    history.textContent = `Last checked: ${date(status.lastChecked)} · Next check: ${date(status.nextCheck)}` +
      (status.lastAttempt ? ` · Last scheduled attempt: ${date(status.lastAttempt)} — ${status.lastResult || 'Unknown'}` : '');
    syncControls();
  }
  async function refresh(manual = false) {
    if (busy || loading || installing || !manual && Date.now() - lastRefresh < 30000) return;
    loading = true; const request = revision;syncControls();
    if(manual)state.textContent = 'Checking for patches for your installed release…';
    try {
      const status = await api(manual?'/api/patch-update/check':'/api/patch-update', manual?{method:'POST'}:{});
      if (!busy && request === revision) render(status);
    } catch (error) { if (!busy && request === revision) state.textContent = error.message; }
    finally { loading = false; lastRefresh = Date.now(); syncControls(); }
  }
  return {init, refresh};
})();
