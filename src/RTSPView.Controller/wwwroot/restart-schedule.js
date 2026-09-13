/* Host scheduling is independent of the browser and portable stream backups. */
const restartScheduleUi = (() => {
  let form, banner, latest, busy = false, refreshing = false, revision = 0;
  function init() {
    form = document.createElement('form');
    form.id = 'restartScheduleForm'; form.className = 'panel control-panel';
    form.innerHTML = `<h2>Scheduled restarts</h2>
      <p>Restart the viewer or Windows host on a schedule. Runs while the Controller is open, even with this browser closed.</p>
      <fieldset class="restart-fields" disabled>
        <label class="toggle-control"><input name="enabled" type="checkbox" role="switch"><span class="toggle-track" aria-hidden="true"></span>Enable scheduled restarts</label>
        <div class="restart-form-grid">
          <label>Action<select name="action"><option value="viewer">Restart viewer application</option><option value="host">Restart Windows host</option></select></label>
          <label>Schedule<select name="mode"><option value="weekly">Selected weekdays</option><option value="interval">Every X hours</option></select></label>
          <label data-mode="interval" hidden>Interval (hours)<input name="intervalHours" type="number" min="1" max="8760" step="1" value="24"></label>
          <label data-mode="weekly">Time on host<input name="time" type="time" value="03:00"></label>
        </div>
        <fieldset class="restart-days" data-mode="weekly"><legend>Days</legend>${['Sun','Mon','Tue','Wed','Thu','Fri','Sat'].map((day,i)=>`<label><input type="checkbox" name="days" value="${i}" ${i===0?'checked':''}>${day}</label>`).join('')}</fieldset>
        <label class="restart-host-warning" hidden><input name="hostAcknowledged" type="checkbox">I understand this restarts the entire Windows host and interrupts every application. RTSPView must be configured to launch after Windows sign-in.</label>
        <div class="actions"><button type="submit">Save schedule</button><button type="button" class="secondary restart-skip" disabled>Skip next restart</button></div>
      </fieldset>
      <p class="restart-timezone"></p>
      <dl class="restart-status"><div><dt>Next restart (host time)</dt><dd data-status="next">—</dd></div><div><dt>Last attempt (host time)</dt><dd data-status="last">—</dd></div><div><dt>Last result</dt><dd data-status="last-result">No attempts yet.</dd></div></dl>
      <p data-status="result">Loading…</p>
      <p class="restart-help">Host restarts have a 60-second countdown with cancellation in the admin panel. Updates defer restarts; missed runs are skipped after startup or sleep. This host-specific schedule is not included in configuration exports.</p>
      <p class="restart-message" role="status"></p>`;
    document.querySelector('#snapshotForm').after(form);
    banner = document.createElement('aside'); banner.className = 'restart-countdown'; banner.hidden = true;
    banner.setAttribute('aria-label','Scheduled host restart');
    const text = document.createElement('span'); text.setAttribute('role','status');
    const cancel = document.createElement('button'); cancel.type = 'button'; cancel.className = 'secondary'; cancel.textContent = 'Cancel this restart'; cancel.onclick = skip;
    banner.append(text,cancel); document.body.append(banner);
    form.addEventListener('change', () => {
      updateFields(); form.querySelector('.restart-message').textContent = 'Unsaved changes.';
    });
    form.querySelector('.restart-skip').onclick = skip;
    form.onsubmit = async event => {
      event.preventDefault();
      const e = form.elements;
      const settings = {enabled:e.enabled.checked,action:e.action.value,mode:e.mode.value,
        intervalHours:Number(e.intervalHours.value),time:e.time.value,
        days:[...form.querySelectorAll('[name=days]:checked')].map(input=>Number(input.value))};
      if (settings.mode==='weekly' && !settings.days.length) { message('Select at least one weekday.'); return; }
      await mutate('/api/restart-schedule','PUT',{settings,hostAcknowledged:e.hostAcknowledged.checked},'Schedule saved.');
      e.hostAcknowledged.checked = false;
    };
    updateFields();
  }
  function message(text) { form.querySelector('.restart-message').textContent = text; }
  function updateFields() {
    const e = form.elements;
    for(const section of form.querySelectorAll('[data-mode]')) section.hidden = section.dataset.mode !== e.mode.value;
    e.intervalHours.required = e.mode.value==='interval'; e.time.required = e.mode.value==='weekly';
    const host = e.enabled.checked && e.action.value==='host';
    form.querySelector('.restart-host-warning').hidden = !host; e.hostAcknowledged.required = host;
  }
  function render(result) {
    latest = result.schedule;
    const hostDate = value => value ? new Date(value).toLocaleString(undefined,{timeZone:result.timeZoneId}) : '—';
    form.querySelector('.restart-timezone').textContent = `Host time zone: ${result.timeZone}`;
    form.querySelector('[data-status=next]').textContent = latest.settings.enabled ? hostDate(latest.pendingUntil||latest.nextRun) : 'Disabled';
    form.querySelector('[data-status=last]').textContent = hostDate(latest.lastRun);
    form.querySelector('[data-status=last-result]').textContent = latest.lastResult || 'No attempts yet.';
    form.querySelector('[data-status=result]').textContent = latest.result;
    form.querySelector('.restart-skip').disabled = busy || !latest.settings.enabled || !latest.nextRun;
    form.querySelector('.restart-skip').textContent = latest.pendingUntil ? 'Cancel this restart' : 'Skip next restart';
    banner.hidden = !latest.pendingUntil;
    if(latest.pendingUntil) banner.querySelector('span').textContent = `Scheduled Windows host restart in ${Math.max(0,Math.ceil((new Date(latest.pendingUntil)-new Date(result.serverTime))/1000))} seconds. All applications will be interrupted.`;
  }
  async function load() {
    try {
      const result = await api('/api/restart-schedule'), s = result.schedule.settings, e = form.elements;
      e.enabled.checked=s.enabled; e.action.value=s.action; e.mode.value=s.mode; e.intervalHours.value=s.intervalHours; e.time.value=s.time;
      for(const input of form.querySelectorAll('[name=days]')) input.checked=s.days.includes(Number(input.value));
      form.querySelector('.restart-fields').disabled=false; updateFields(); render(result);
    } catch(error) { message('Schedule unavailable: '+error.message); }
  }
  async function refresh() {
    if (busy || refreshing) return;
    refreshing = true; const current = revision;
    try { const result = await api('/api/restart-schedule'); if(current===revision)render(result); }
    catch { if(current===revision){banner.hidden=true; form.querySelector('[data-status=result]').textContent='Schedule status unavailable. Reconnect to the Controller to check pending restarts.';} }
    finally { refreshing=false; }
  }
  async function mutate(url,method,body,success) {
    if(busy)return; busy=true; revision++;
    form.querySelector('.restart-fields').disabled=true; banner.querySelector('button').disabled=true;
    try { render(await api(url,{method,...(body?{body:JSON.stringify(body)}:{})})); message(success); }
    catch(error) { message(error.message); }
    finally { busy=false; form.querySelector('.restart-fields').disabled=false; banner.querySelector('button').disabled=false; if(latest)form.querySelector('.restart-skip').disabled=!latest.settings.enabled||!latest.nextRun; }
  }
  async function skip() { await mutate('/api/restart-schedule/skip','POST',null,'Scheduled restart skipped.'); }
  function hide() { revision++; if(banner)banner.hidden=true; }
  return {init,load,refresh,hide};
})();
