const pluginsUi = (() => {
  const names={weather:'Weather',aircraft:'Aircraft',ytDlp:'yt-dlp',onvif:'ONVIF',streamlink:'Streamlink',pictureInPicture:'Picture in picture',automations:'Automations'};
  let flags = {};
  let loaded=false, checking=false;
  const enabled = name => flags[name] !== false;
  function load(config) {
    flags = {...config.plugins};
    loaded=true;
    for (const name of Object.keys(names)) {
      document.documentElement.dataset[name+'Enabled'] = String(enabled(name));
      const input = document.querySelector('#pluginsUiForm [name="'+name+'"]');
      if (input) input.checked = enabled(name);
    }
    if(!enabled('automations'))document.querySelector('[data-layout-tab="standard"]')?.click();
    if(!enabled('automations')&&document.querySelector('[data-page="automation"][aria-current="page"]')||!enabled('pictureInPicture')&&document.querySelector('[data-page="overlays"][aria-current="page"]'))adminLayout.select('overview');
  }
  setInterval(async()=>{
    if(!loaded||checking||document.hidden)return;
    checking=true;
    try{const latest=await api('/api/plugins');if(Object.keys(names).some(k=>(latest[k]!==false)!==enabled(k)))await window.reloadPlugins();}
    catch{/* Preserve the last confirmed configuration while disconnected. */}
    finally{checking=false;}
  },5000);
  function sourceControls(form) {
    if(!enabled('automations')){
      const hiddenMode=form.querySelector('select[name="enabled"] option[value="false"]');
      if(hiddenMode)hiddenMode.textContent='Hidden';
    }
    const source=form.elements.sourceMode;
    for(const [value,key] of [['2','streamlink'],['3','ytDlp']]){
      const option=source.querySelector('option[value="'+value+'"]');
      if(option&&!enabled(key)){if(source.value===value){option.textContent='Saved source (disabled)';option.disabled=true;}else option.remove();}
    }
    if(!enabled('ytDlp')||!enabled('streamlink')){
      const help=form.querySelector('.source-type-help-content');
      if(help)help.textContent='Auto plays direct media URLs and uses enabled source plugins for website links. Direct stream bypasses website detection.';
    }
    if(!enabled('ytDlp')&&!enabled('streamlink'))form.elements.maximumHeight.closest('label').hidden=true;
  }
  function panel() {
    const form = document.createElement('form'); form.id='pluginsUiForm'; form.className='control-panel panel';
    form.innerHTML='<h2>Plugins</h2><p>Turn plugins on or off for this host. Disabled plugins stop running and their controls are hidden. Saved settings are kept for when you turn them back on.</p><div class="display-options">'+Object.entries(names).map(([id,label])=>'<label><span class="switch"><input type="checkbox" role="switch" name="'+id+'" aria-label="'+label+'"><span></span></span>'+label+'</label>').join('')+'</div><div class="actions"><span role="status"></span><button type="submit">Apply changes</button></div>';
    form.onsubmit=async event=>{
      event.preventDefault(); const status=form.querySelector('[role="status"]'),button=form.querySelector('button');button.disabled=true;
      try {
        status.textContent='Saving…';
        const saved=await api('/api/plugins',{method:'PUT',body:JSON.stringify(Object.fromEntries(Object.keys(names).map(k=>[k,form.elements[k].checked])))});
        load({plugins:saved});
        await window.reloadPlugins();
        status.textContent='Applied. Saved feature settings are preserved.';
      } catch(error) { status.textContent=error.message; }
      finally {button.disabled=false;}
    };
    return form;
  }
  return {enabled,load,panel,sourceControls};
})();
