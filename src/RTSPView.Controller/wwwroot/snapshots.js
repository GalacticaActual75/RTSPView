/* Captures use the existing viewer-command endpoint; scheduling belongs to the host. */
const snapshotUi = (() => {
  let streams = [], busy = false;
  const buttons = [];
  function init() {
    const panel = document.createElement('form'); panel.id = 'snapshotForm'; panel.className = 'panel control-panel';
    panel.innerHTML = '<h2>Browser snapshots</h2><p>Previews are snapshots, not live video. A new snapshot is captured when each stream starts. Refresh on demand or let the viewer refresh them on a schedule, even with this browser closed.</p><label class="toggle-control"><input name="enabled" type="checkbox" role="switch"><span class="toggle-track" aria-hidden="true"></span>Automatically refresh snapshots</label><div class="display-field-row"><label>Refresh every (hours)<input name="intervalHours" type="number" min="0.1" max="168" step="0.1" required value="1"></label><div class="actions"><button type="submit">Apply changes</button></div></div><p>0.1–168 hours (6 minutes to 7 days). The viewer must be running. Unavailable streams keep their last successful snapshot.</p><p id="snapshotSettingsState" role="status"></p>';
    document.querySelector('#displayForm').after(panel);
    panel.oninput=()=>{panel.dataset.dirty='true';document.querySelector('#snapshotSettingsState').textContent='Unsaved changes — Apply changes updates the capture schedule';};
    panel.onsubmit = async event => {
      event.preventDefault(); const button = panel.querySelector('[type=submit]'), state = document.querySelector('#snapshotSettingsState');
      button.disabled = true; state.textContent = 'Saving…';
      try { const result = await api('/api/snapshots/settings', {method:'PUT',body:JSON.stringify({enabled:panel.elements.enabled.checked,intervalHours:Number(panel.elements.intervalHours.value)})}); apply(result); state.textContent = 'Applied — snapshot schedule updated.'; }
      catch(error) { state.textContent = error.message; }
      finally { button.disabled = false; }
    };
    for (const parent of [document.querySelector('#page-overview .section-title'), document.querySelector('.camera-page-toolbar'), panel]) {
      const wrapper = document.createElement('div'); wrapper.className = 'snapshot-action';
      const button = document.createElement('button'); button.type = 'button'; button.className = 'secondary'; button.textContent = 'Refresh snapshots'; button.onclick = refresh;
      const state = document.createElement('span'); state.className = 'save-state'; state.setAttribute('role','status'); wrapper.append(button,state); parent.insertBefore(wrapper,parent.querySelector('#addCamera')); buttons.push({button,state});
    }
  }
  function apply(settings) { const form = document.querySelector('#snapshotForm');form.dataset.dirty='false'; form.elements.enabled.checked = settings?.enabled ?? false; form.elements.intervalHours.value = settings?.intervalHours ?? 1; }
  function load(config) { apply(config.snapshots); }
  function updated(slot) {
    const path = `/api/cameras/${slot}/thumbnail`;
    for (const image of document.querySelectorAll('.feed-thumbnail,.designer-tile img,.designer-camera img')) {
      if (Number(image.dataset.snapshotSlot)!==slot && new URL(image.src,location.href).pathname !== path) continue;
      dashboardUX.snapshot(image,slot);
    }
    for (const form of document.querySelectorAll('.camera-card')) form.dispatchEvent(new CustomEvent('snapshot-updated',{detail:slot}));
  }
  async function refresh() {
    if (busy) return; busy = true; let succeeded = 0; const failed = [];
    for (const {button,state} of buttons) { button.disabled = true; state.textContent = 'Refreshing…'; }
    try {
      const config = await api('/api/config');
      streams = [...config.cameras.slice(0,config.cameraCount||9),config.doorbellOverlay.camera,config.garageOverlay.camera,...(config.additionalOverlays||[]).map(o=>o.camera)].filter(c=>c.enabled&&c.rtspUrl);
      for (const stream of streams) {
        for(const {state} of buttons)state.textContent=`Capturing ${succeeded+failed.length+1} of ${streams.length}: ${stream.name}…`;
        try { await api(`/api/cameras/${stream.slot}/thumbnail/refresh`,{method:'POST'}); succeeded++; updated(stream.slot); }
        catch { failed.push(stream.name); }
      }
      const message = !streams.length ? 'No enabled, configured streams.' : `${succeeded} of ${streams.length} snapshots refreshed.` + (failed.length ? ` Unavailable: ${failed.join(', ')}. Previous snapshots retained.` : '');
      for(const {state} of buttons){state.textContent = message;state.dataset.tone=failed.length?'warning':'healthy';}
    } catch(error) { for(const {state} of buttons){state.textContent = error.message;state.dataset.tone='error';} }
    finally { busy = false; for(const {button} of buttons)button.disabled = false; }
  }
  return {init,load,updated};
})();
