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
    const menu = document.createElement('div'); menu.className = 'quick-actions';
    const summary = document.createElement('button'); summary.type = 'button'; summary.className = 'secondary'; summary.textContent = 'Quick actions'; summary.setAttribute('aria-expanded', 'false'); summary.setAttribute('aria-controls', 'quickActionsPanel'); menu.append(summary);
    const content = document.createElement('div'); content.className = 'quick-actions-panel'; content.id = 'quickActionsPanel'; content.hidden = true; menu.append(content);
    const close = () => { content.hidden = true; summary.setAttribute('aria-expanded', 'false'); };
    summary.onclick = () => { content.hidden = !content.hidden; summary.setAttribute('aria-expanded', String(!content.hidden)); };
    document.addEventListener('click', event => { if (!menu.contains(event.target)) close(); });
    menu.addEventListener('keydown', event => { if (event.key === 'Escape') { close(); summary.focus(); } });
    menu.addEventListener('focusout', event => { if (!menu.contains(event.relatedTarget)) close(); });
    for (const source of document.querySelectorAll('.viewer-display-panel [data-action], .viewer-display-panel [data-system], .viewer-display-panel [data-application]')) {
      const button = source.cloneNode(true); button.removeAttribute('aria-describedby'); button.type = 'button'; content.append(button);
    }
    const status = document.createElement('p'); status.id = 'quickControlState'; status.setAttribute('role', 'status'); content.append(status);
    document.querySelector('#logout').before(menu);
    const style = document.createElement('style'); style.textContent = 'header .quick-actions{position:relative;flex-shrink:0;margin:0;padding:0;border:0;background:none}.quick-actions>button{display:inline-flex;align-items:center;gap:10px;margin:0}.quick-actions>button::after{content:"";width:6px;height:6px;border-right:2px solid currentColor;border-bottom:2px solid currentColor;transform:translateY(-2px) rotate(45deg)}.quick-actions>button[aria-expanded="true"]::after{transform:translateY(2px) rotate(225deg)}.quick-actions-panel[hidden]{display:none}.quick-actions-panel{position:absolute;right:0;top:calc(100% + 8px);width:260px;max-width:calc(100vw - 32px);padding:12px;background:var(--panel,#171b20);border:1px solid var(--line);border-radius:6px;box-shadow:0 12px 30px #0008;z-index:100}.quick-actions-panel button{display:block;width:100%;margin:0 0 8px;text-align:left}.quick-actions-panel p{font-size:12px;overflow-wrap:anywhere;margin:8px 0 0}'; document.head.append(style);
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
    for (const button of document.querySelectorAll('[data-application], [data-system]')) button.disabled = pending;
    document.querySelector('#viewerLifecycleState').textContent = s.lifecycle;
    const hint = document.querySelector('#enter-fullscreen-help'); if (hint) hint.textContent = s.action === 'exit-fullscreen' ? 'Return to a window. Always-on-top remains a separate setting.' : 'Fill the selected display with the live wall.';
  }
  function message(text) { for (const id of ['controlState', 'quickControlState']) document.getElementById(id).textContent = text; }
  return {init, update, state, message, busy(value) { pending = value; update(latest); }};
})();
