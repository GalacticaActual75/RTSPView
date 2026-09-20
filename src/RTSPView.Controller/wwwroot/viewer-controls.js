const viewerControls = (() => {
  let latest = null, pending = false;
  function state(t) {
    const running = t?.viewerRunning === true, starting = t?.viewerStarting === true;
    const connected = running && !starting && t?.viewerConnected === true;
    const full = connected && typeof t?.viewer?.isFullScreen === 'boolean' ? t.viewer.isFullScreen : null;
    return {start: !!t && !running && !starting, restart: running && !starting,
      streams: connected, fullscreen: full !== null, action: full === true ? 'exit-fullscreen' : 'enter-fullscreen',
      label: full === true ? 'Exit full screen' : 'Enter full screen',
      lifecycle: !t ? 'Viewer status unavailable.' : t.viewerPaused ? 'Viewer intentionally stopped. Automatic recovery is paused.' : starting ? 'Viewer is starting…' : running ? 'Viewer running. Automatic recovery is enabled.' : 'Viewer offline. Automatic recovery is enabled.'};
  }
  function init() {
    const menu = document.createElement('details'); menu.className = 'designer-menu quick-actions';
    const summary = document.createElement('summary'); summary.textContent = 'Quick actions'; menu.append(summary);
    const content = document.createElement('div'); content.className = 'quick-actions-panel'; menu.append(content);
    for (const source of document.querySelectorAll('.viewer-display-panel [data-action], .viewer-display-panel [data-system]')) {
      const button = source.cloneNode(true); button.removeAttribute('aria-describedby'); button.type = 'button'; content.append(button);
    }
    const status = document.createElement('p'); status.id = 'quickControlState'; status.setAttribute('role', 'status'); content.append(status);
    document.querySelector('#logout').before(menu);
    const style = document.createElement('style'); style.textContent = 'header .quick-actions{position:relative;flex-shrink:0;margin:0;padding:0;border:0;background:none}.quick-actions>summary{cursor:pointer;padding:9px 12px;border:1px solid var(--line);border-radius:5px;list-style:none}.quick-actions-panel{position:absolute;right:0;top:calc(100% + 8px);width:260px;max-width:calc(100vw - 32px);padding:12px;background:var(--panel,#171b20);border:1px solid var(--line);border-radius:6px;box-shadow:0 12px 30px #0008;z-index:100}.quick-actions-panel button{display:block;width:100%;margin:0 0 8px;text-align:left}.quick-actions-panel p{font-size:12px;overflow-wrap:anywhere;margin:8px 0 0}'; document.head.append(style);
    update(null);
  }
  function update(t) {
    latest = t; const s = state(t);
    for (const button of document.querySelectorAll('[data-action]')) {
      if (button.hasAttribute('data-fullscreen-toggle')) {
        button.dataset.action = s.action; button.textContent = s.label; button.disabled = pending || !s.fullscreen;
        button.title = s.fullscreen ? s.label : 'Waiting for the running viewer’s full-screen status';
      } else if (button.dataset.action === 'start') {
        button.disabled = pending || !s.start; button.textContent = t?.viewerStarting ? 'Starting viewer…' : 'Start viewer';
      } else if (button.dataset.action === 'restart') button.disabled = pending || !s.restart;
      else if (button.dataset.action === 'restart-cameras') button.disabled = pending || !s.streams;
    }
    document.querySelector('#viewerLifecycleState').textContent = s.lifecycle;
    const hint = document.querySelector('#enter-fullscreen-help'); if (hint) hint.textContent = s.action === 'exit-fullscreen' ? 'Return to a window. Always-on-top remains a separate setting.' : 'Fill the selected display with the live wall.';
  }
  function message(text) { for (const id of ['controlState', 'quickControlState']) document.getElementById(id).textContent = text; }
  return {init, update, state, message, busy(value) { pending = value; update(latest); }};
})();
