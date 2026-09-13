const temperatureUi = (() => {
  let form, busy=false, refreshing=false, revision=0;
  const format=value=>typeof value==='number'&&Number.isFinite(value)?`${value.toFixed(1)} °C`:'Unavailable';
  function init(){
    form=document.createElement('form');form.id='temperatureForm';form.className='panel control-panel';
    form.innerHTML=`<h2>Temperature monitoring</h2><p>Current temperatures refresh every five seconds. Set separate limits for CPU and GPU warnings on the live view.</p>
      <fieldset class="temperature-fields" disabled>
        <label class="toggle-control"><input name="showWarnings" type="checkbox" role="switch"><span class="toggle-track" aria-hidden="true"></span>Show temperature warnings on the live view</label>
        <p>Turn this off to hide all temperature warnings. Current readings and saved limits remain available.</p>
        <div class="temperature-grid">${['cpu','gpu'].map(kind=>`<section class="temperature-device" aria-label="${kind.toUpperCase()} temperature settings"><h3>${kind.toUpperCase()}</h3><p class="temperature-reading" data-temperature="${kind}">Unavailable</p><label class="toggle-control"><input name="${kind}WarningEnabled" type="checkbox" role="switch"><span class="toggle-track" aria-hidden="true"></span>Warn for ${kind.toUpperCase()}</label><label>Maximum ${kind.toUpperCase()} temperature (°C)<input name="${kind}MaxC" type="number" min="1" max="150" step="0.1" value="${kind==='cpu'?90:85}" required></label></section>`).join('')}</div>
        <div class="actions"><span class="temperature-message" role="status"></span><button>Save temperature settings</button></div>
      </fieldset><p class="information-note">Shows the hottest reported temperature for each device type, including GPU hotspots or memory sensors when exposed. Choose limits appropriate for your hardware. Missing sensors show Unavailable and cannot trigger a warning. These settings apply only to this host.</p>
      <section aria-labelledby="temperature-dependencies-title">
        <h3 id="temperature-dependencies-title">Sensor requirements</h3>
        <p>CPU temperature readings may require the PawnIO driver and administrator access. Use the host installation controls above, or install its official signed edition manually on the computer running RTSPView and restart the Controller.</p>
        <p><a href="https://pawnio.eu/" target="_blank" rel="noopener noreferrer">Download PawnIO (official site, opens in a new tab)</a></p>
        <p>LibreHardwareMonitor is included with RTSPView; no separate download is needed. The optional maintenance helper installs only the verified PawnIO package and reads sensors. UAC stays enabled.</p>
      </section>`;
    document.querySelector('#displayForm').after(form);
    form.addEventListener('change',()=>message('Unsaved changes.'));
    form.onsubmit=async event=>{
      event.preventDefault();if(busy)return;busy=true;revision++;
      const e=form.elements,settings={showWarnings:e.showWarnings.checked,cpuWarningEnabled:e.cpuWarningEnabled.checked,gpuWarningEnabled:e.gpuWarningEnabled.checked,cpuMaxC:Number(e.cpuMaxC.value),gpuMaxC:Number(e.gpuMaxC.value)};
      form.querySelector('fieldset').disabled=true;
      try{render(await api('/api/temperatures',{method:'PUT',body:JSON.stringify(settings)}));message('Temperature settings saved.');}
      catch(error){message(error.message);}
      finally{busy=false;form.querySelector('fieldset').disabled=false;}
    };
  }
  function message(text){form.querySelector('.temperature-message').textContent=text;}
  function render(status){
    for(const kind of ['cpu','gpu']){
      const value=status[kind+'C'],reading=form.querySelector(`[data-temperature=${kind}]`);
      reading.textContent=format(value);
      reading.classList.toggle('temperature-high',typeof value==='number'&&value>status.settings[kind+'MaxC']);
    }
  }
  async function load(){
    try{const status=await api('/api/temperatures');for(const [name,value] of Object.entries(status.settings)){const field=form.elements[name];if(!field)continue;if(field.type==='checkbox')field.checked=value;else field.value=value;}render(status);form.querySelector('fieldset').disabled=false;}
    catch(error){message('Temperature settings unavailable: '+error.message);}
  }
  async function refresh(){
    if(busy||refreshing)return;refreshing=true;const current=revision;
    try{const status=await api('/api/temperatures');if(current===revision)render(status);}
    catch{if(current===revision)for(const reading of form.querySelectorAll('[data-temperature]')){reading.textContent='Unavailable';reading.classList.remove('temperature-high');}}
    finally{refreshing=false;}
  }
  return{init,load,refresh,format};
})();
