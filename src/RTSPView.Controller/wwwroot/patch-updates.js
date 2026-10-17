const patchUpdates = (() => {
  let toggle, state, versions, history, install, latest, busy = false, loading = false, lastRefresh = 0, revision = 0;
  function init() {
    const section = document.createElement('section');
    section.className = 'patch-updates';
    section.innerHTML = `<h3>Application patches</h3>
      <label class="toggle-control"><input type="checkbox" role="switch" disabled><span class="toggle-track" aria-hidden="true"></span>Automatically install patches during scheduled restarts</label>
      <p>Includes dashboard fixes and Windows application fixes for your current release. Off by default. When enabled, patches install during the Live View restart schedule in Maintenance and restart the whole application. An enabled, up-to-date maintenance helper is required. No schedule means no automatic installation.</p>
      <p>Turning this off keeps daily checks and manual installation available. It also cancels pending automatic installation, unless the installer has already started. Full version upgrades still require confirmation.</p>
      <p class="patch-versions"></p><p class="patch-state" role="status">Loading patch settings…</p><p class="patch-history"></p>
      <div class="actions"><button type="button" class="secondary patch-install" hidden>Install patch now</button></div>`;
    document.querySelector('#updatePanel').append(section);
    toggle = section.querySelector('input'); state = section.querySelector('.patch-state');
    versions = section.querySelector('.patch-versions'); install = section.querySelector('.patch-install');
    history = section.querySelector('.patch-history');
    toggle.onchange = async () => {
      busy = true; toggle.disabled = true; revision++;
      try { render(await api('/api/patch-update/automatic', {method:'PUT', body:JSON.stringify({enabled:toggle.checked})})); }
      catch (error) { toggle.checked = latest?.autoInstall ?? false; state.textContent = error.message; }
      finally { busy = false; toggle.disabled = false; }
    };
    install.onclick = async () => {
      const version = latest?.availableVersion;
      if (!version || !await uiDialogs.ask('Install this patch now? The stream wall and dashboard will briefly disconnect while the application restarts.')) return;
      busy = true; install.disabled = true; revision++;
      try {
        const result = await api('/api/update/install', {method:'POST', body:JSON.stringify({confirmed:true,channel:version.includes('-beta.')?'beta':'stable',version})});
        state.textContent = result.message; install.hidden = true;
      } catch (error) { state.textContent = error.message; }
      finally { busy = false; install.disabled = false; }
    };
  }
  function render(status) {
    latest = status; toggle.checked = !!status.autoInstall;
    const describe = version => version?.replace(/-patch\.(\d+)$/, ' · patch $1') || 'None';
    versions.textContent = `Installed: ${describe(status.installedVersion)} · Available patch: ${describe(status.availableVersion)}`;
    state.textContent = status.message;
    const date = value => value ? new Date(value).toLocaleString() : 'Pending';
    history.textContent = `Last checked: ${date(status.lastChecked)} · Next check: ${date(status.nextCheck)}` +
      (status.lastAttempt ? ` · Last scheduled attempt: ${date(status.lastAttempt)} — ${status.lastResult || 'Unknown'}` : '');
    install.hidden = !status.availableVersion;
  }
  async function refresh(manual = false) {
    if (busy || loading || !manual && Date.now() - lastRefresh < 30000) return;
    loading = true; const request = revision;
    try {
      const status = await api(manual?'/api/patch-update/check':'/api/patch-update', manual?{method:'POST'}:{});
      if (!busy && request === revision) { render(status); toggle.disabled = false; }
    } catch (error) { if (!busy && request === revision) state.textContent = error.message; }
    finally { loading = false; lastRefresh = Date.now(); }
  }
  return {init, refresh};
})();
