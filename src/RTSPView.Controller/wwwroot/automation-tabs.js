const automationTabs = (() => {
  let root, panel, list, message, save, state, dirty = false, busy = false, dragged;
  const otherDirty = () => ['automationForm', 'tapoForm'].some(id => document.getElementById(id)?.dataset.dirty === 'true');
  function init() {
    if (root) return;
    root = document.querySelector('#page-automation');
    const nav = document.createElement('div'); nav.className = 'automation-tabs'; nav.setAttribute('role', 'tablist'); nav.setAttribute('aria-label', 'Automation integration'); root.prepend(nav);
    panel = document.createElement('section'); panel.id = 'automationPriority'; panel.innerHTML = '<h2>Automation priority</h2><p>Top is highest priority. Drag rules or use Move up/down, then Save order. Priorities apply across MQTT and Tapo. Disabled rules keep their place.</p><p>Priority 1 is highest. Higher-priority rules interrupt lower-priority views immediately. After they clear, a still-active lower-priority rule can resume. Manual camera focus takes priority over automation.</p><ol class="priority-list"></ol><div class="control-buttons"><button type="button" class="priority-save">Save order</button><button type="button" class="secondary priority-reload">Discard & reload</button></div><p class="priority-message" role="status"></p>'; root.append(panel);
    list = panel.querySelector('ol'); message = panel.querySelector('.priority-message'); save = panel.querySelector('.priority-save');
    const panes = [document.querySelector('#automationForm'), document.querySelector('#tapoForm'), panel];
    for (const [index, title] of ['MQTT', 'Tapo', 'Automation Priority'].entries()) {
      const pane = panes[index]; pane.setAttribute('role', 'tabpanel'); pane.setAttribute('aria-labelledby', 'automation-tab-' + index);
      const tab = document.createElement('button'); tab.type = 'button'; tab.id = 'automation-tab-' + index; tab.textContent = title; tab.setAttribute('role', 'tab'); tab.setAttribute('aria-controls', pane.id);
      tab.onclick = () => { panes.forEach((p, i) => { p.hidden = i !== index; nav.children[i].setAttribute('aria-selected', String(i === index)); nav.children[i].tabIndex = i === index ? 0 : -1; }); if (index === 2) load(); };
      tab.onkeydown = e => { if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(e.key)) { e.preventDefault(); const n = e.key === 'Home' ? 0 : e.key === 'End' ? 2 : (index + (e.key === 'ArrowRight' ? 1 : 2)) % 3; nav.children[n].click(); nav.children[n].focus(); } }; nav.append(tab);
    }
    save.onclick = persist; panel.querySelector('.priority-reload').onclick = () => { dirty = false; panel.dataset.dirty = 'false'; load(); };
    nav.children[0].click();
  }
  function render() {
    list.replaceChildren();
    for (const [i, rule] of (state?.rules || []).entries()) {
      const row = document.createElement('li'); row.draggable = true; row.className = 'priority-row';
      const label = document.createElement('span'); label.textContent = `Priority ${i + 1} · ${rule.name} · ${rule.source}${rule.enabled ? '' : ' · Disabled'}`; row.append(label);
      for (const [delta, text] of [[-1, 'Move up'], [1, 'Move down']]) { const b = document.createElement('button'); b.type = 'button'; b.className = 'secondary'; b.textContent = text; b.setAttribute('aria-label', `${text}: ${rule.name} (${rule.source})`); b.disabled = busy || i + delta < 0 || i + delta >= state.rules.length; b.onclick = () => move(i, i + delta); row.append(b); }
      row.ondragstart = e => { if (busy) { e.preventDefault(); return; } dragged = i; e.dataTransfer.effectAllowed = 'move'; e.dataTransfer.setData('text/plain', String(i)); };
      row.ondragover = e => e.preventDefault(); row.ondrop = e => { e.preventDefault(); if (Number.isInteger(dragged)) move(dragged, i); dragged = undefined; }; row.ondragend = () => { dragged = undefined; }; list.append(row);
    }
    if (!state?.rules.length) list.textContent = 'No saved automations yet. Add rules under MQTT or Tapo.';
    save.disabled = busy || !dirty || otherDirty();
  }
  function move(from, to) { if (busy || otherDirty()) { message.textContent = 'Save or discard MQTT/Tapo edits before changing priority.'; return; } const [rule] = state.rules.splice(from, 1); state.rules.splice(to, 0, rule); dirty = true; panel.dataset.dirty = 'true'; render(); message.textContent = 'Unsaved order. Top rule will have priority 1.'; }
  async function load() {
    if (busy) return;
    if (otherDirty()) { message.textContent = 'Save or discard MQTT/Tapo edits before changing priority.'; save.disabled = true; return; }
    if (dirty) { render(); return; }
    busy = true;
    try { state = await api('/api/automation/priorities'); dirty = state.rules.some((rule, index) => rule.priority !== index + 1); panel.dataset.dirty = String(dirty); message.textContent = dirty ? 'Save order to apply the displayed positions as priorities. Existing priorities need updating.' : 'Saved order. Priority follows each rule’s position: 1 is highest.'; }
    catch (e) { message.textContent = e.message; }
    finally { busy = false; render(); }
  }
  async function persist() {
    if (busy || !dirty || otherDirty()) return; busy = true; render(); panel.inert = true;
    try { state = await api('/api/automation/priorities', {method: 'PUT', body: JSON.stringify(state)}); dirty = false; panel.dataset.dirty = 'false'; await automationUi.load(); message.textContent = 'Priority order saved.'; }
    catch (e) { message.textContent = e.message; }
    finally { busy = false; panel.inert = false; render(); }
  }
  return {init};
})();
