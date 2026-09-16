/* Independent host-local schedules; tab switches never save or reset fields. */
const restartScheduleUi = (() => {
  const forms = {};
  let banner, latest, busy = false, refreshing = false, revision = 0;
  function init() {
    const section = document.createElement('section');
    section.id = 'restartScheduleForm'; section.className = 'panel control-panel';
    section.innerHTML = `<h2>Scheduled restarts</h2><p>Schedule the viewer application, Windows host, or both independently. Runs while the Controller is open, even with this browser closed.</p><div class="restart-schedules"></div><p class="restart-help">Host restarts have a 60-second countdown with cancellation in the admin panel. Updates defer restarts; missed runs are skipped after startup or sleep. If both actions are due together, the host restart takes priority. These host-specific schedules are not included in configuration exports.</p>`;
    for (const [action,title,help] of [['viewer','Viewer application','Restart RTSPView without restarting Windows.'],['host','Windows host','Restart Windows and all applications on this computer.']]) {
      const form = document.createElement('form'); forms[action] = form; form.className = 'restart-schedule-card';
      form.setAttribute('aria-labelledby',`restart-title-${action}`);
      form.innerHTML = `<h3 id="restart-title-${action}">${title}</h3><p>${help}</p>
        <fieldset class="restart-fields" disabled>
          <label class="toggle-control"><input name="enabled" type="checkbox" role="switch"><span class="toggle-track" aria-hidden="true"></span>Enable ${action} restarts</label>
          <div class="restart-form-grid">
            <label>Schedule<select name="mode"><option value="weekly">Selected weekdays</option><option value="interval">Every X hours</option></select></label>
            <label data-mode="interval" hidden>Interval (hours)<input name="intervalHours" type="number" min="1" max="8760" step="1" value="24"></label>
            <label data-mode="weekly">Time on host<input name="time" type="time" value="03:00"></label>
          </div>
          <fieldset class="restart-days" data-mode="weekly"><legend>Days</legend>${['Sun','Mon','Tue','Wed','Thu','Fri','Sat'].map((day,i)=>`<label><input type="checkbox" name="days" value="${i}" ${i===0?'checked':''}>${day}</label>`).join('')}</fieldset>
          ${action==='host'?'<label class="restart-host-warning" hidden><input name="hostAcknowledged" type="checkbox">I understand this restarts the entire Windows host and interrupts every application. RTSPView must be configured to launch after Windows sign-in.</label>':''}
          <div class="actions"><button type="submit">Apply changes</button><button type="button" class="secondary restart-skip" disabled>Skip next ${action} restart</button></div>
        </fieldset>
        <p class="restart-timezone"></p>
        <dl class="restart-status"><div><dt>Next restart (host time)</dt><dd data-status="next">—</dd></div><div><dt>Last attempt (host time)</dt><dd data-status="last">—</dd></div><div><dt>Last result</dt><dd data-status="last-result">No attempts yet.</dd></div></dl>
        <p class="restart-current" role="status"></p><p class="restart-message" role="status">Loading…</p>`;
      section.querySelector('.restart-schedules').append(form);
      form.addEventListener('change',event=>{if(event.target.name!=='hostAcknowledged'&&form.elements.hostAcknowledged){form.dataset.acknowledged='false';form.elements.hostAcknowledged.checked=false;}updateFields(action);message(action,'Unsaved changes.');});
      form.querySelector('.restart-skip').onclick=()=>skip(action);
      form.addEventListener('input',event=>{if(event.target.name!=='hostAcknowledged'&&form.elements.hostAcknowledged){form.dataset.acknowledged='false';form.elements.hostAcknowledged.checked=false;updateFields(action);}form.dataset.dirty='true';message(action,'Unsaved changes — Apply changes updates this restart schedule');});
      form.onsubmit=async event=>{
        event.preventDefault(); const e=form.elements;
        const settings={enabled:e.enabled.checked,action,mode:e.mode.value,intervalHours:Number(e.intervalHours.value),time:e.time.value,days:[...form.querySelectorAll('[name=days]:checked')].map(input=>Number(input.value))};
        if(settings.mode==='weekly'&&!settings.days.length){message(action,'Select at least one weekday.');return;}
        const saved=await mutate(action,'/api/restart-schedule','PUT',{settings,hostAcknowledged:!!e.hostAcknowledged?.checked});
        if(saved){form.dataset.dirty='false';message(action,'Applied — restart schedule updated.');if(e.hostAcknowledged){form.dataset.acknowledged=String(e.enabled.checked);e.hostAcknowledged.checked=e.enabled.checked;updateFields(action);}}
      };
      updateFields(action);
    }
    document.querySelector('#snapshotForm').after(section);
    banner=document.createElement('aside');banner.className='restart-countdown';banner.hidden=true;banner.setAttribute('aria-label','Scheduled host restart');
    const text=document.createElement('span');text.setAttribute('role','status');
    const cancel=document.createElement('button');cancel.type='button';cancel.className='secondary';cancel.textContent='Cancel this restart';cancel.onclick=()=>skip('host');
    banner.append(text,cancel);document.body.append(banner);
  }
  function message(action,text){forms[action].querySelector('.restart-message').textContent=text;}
  function updateFields(action){
    const form=forms[action],e=form.elements;
    for(const section of form.querySelectorAll('[data-mode]'))section.hidden=section.dataset.mode!==e.mode.value;
    e.intervalHours.required=e.mode.value==='interval';e.time.required=e.mode.value==='weekly';
    if(action==='host'){form.querySelector('.restart-host-warning').hidden=!e.enabled.checked||form.dataset.acknowledged==='true';e.hostAcknowledged.required=e.enabled.checked&&form.dataset.acknowledged!=='true';}
  }
  function render(result){
    latest=result.schedules;
    const hostDate=value=>value?new Date(value).toLocaleString(undefined,{timeZone:result.timeZoneId}):'—';
    for(const [action,form] of Object.entries(forms)){
      const state=latest[action];
      form.querySelector('.restart-timezone').textContent=`Host time zone: ${result.timeZone}`;
      form.querySelector('[data-status=next]').textContent=state.settings.enabled?hostDate(state.pendingUntil||state.nextRun):'Disabled';
      form.querySelector('[data-status=last]').textContent=hostDate(state.lastRun);
      form.querySelector('[data-status=last-result]').textContent=state.lastResult||'No attempts yet.';
      form.querySelector('.restart-current').textContent=state.result;
      form.querySelector('.restart-skip').disabled=busy||!state.settings.enabled||!state.nextRun;
      form.querySelector('.restart-skip').textContent=state.pendingUntil?'Cancel this restart':`Skip next ${action} restart`;
    }
    banner.hidden=!latest.host.pendingUntil;
    if(latest.host.pendingUntil)banner.querySelector('span').textContent=`Scheduled Windows host restart in ${Math.max(0,Math.ceil((new Date(latest.host.pendingUntil)-new Date(result.serverTime))/1000))} seconds. All applications will be interrupted.`;
  }
  async function load(){
    try{
      const result=await api('/api/restart-schedule');
      for(const [action,form] of Object.entries(forms)){
        const s=result.schedules[action].settings,e=form.elements;
        if(e.hostAcknowledged){form.dataset.acknowledged=String(s.enabled);e.hostAcknowledged.checked=s.enabled;}e.enabled.checked=s.enabled;e.mode.value=s.mode;e.intervalHours.value=s.intervalHours;e.time.value=s.time;
        for(const input of form.querySelectorAll('[name=days]'))input.checked=s.days.includes(Number(input.value));
        form.querySelector('.restart-fields').disabled=false;updateFields(action);message(action,'');
      }
      render(result);
    }catch(error){for(const action of Object.keys(forms))message(action,'Schedules unavailable: '+error.message);}
  }
  async function refresh(){
    if(busy||refreshing)return;refreshing=true;const current=revision;
    try{const result=await api('/api/restart-schedule');if(current===revision)render(result);}
    catch{if(current===revision){banner.hidden=true;for(const form of Object.values(forms))form.querySelector('.restart-current').textContent='Schedule status unavailable. Reconnect to the Controller to check pending restarts.';}}
    finally{refreshing=false;}
  }
  async function mutate(action,url,method,body){
    if(busy)return false;busy=true;revision++;
    for(const form of Object.values(forms))form.querySelector('.restart-fields').disabled=true;
    banner.querySelector('button').disabled=true;
    try{render(await api(url,{method,...(body?{body:JSON.stringify(body)}:{})}));if(body)message(action,'');return true;}
    catch(error){message(action,error.message);return false;}
    finally{
      busy=false;banner.querySelector('button').disabled=false;
      for(const [key,form] of Object.entries(forms)){form.querySelector('.restart-fields').disabled=false;if(latest)form.querySelector('.restart-skip').disabled=!latest[key].settings.enabled||!latest[key].nextRun;}
    }
  }
  async function skip(action){await mutate(action,`/api/restart-schedule/skip?action=${action}`,'POST',null);}
  function hide(){revision++;if(banner)banner.hidden=true;}
  return{init,load,refresh,hide};
})();
