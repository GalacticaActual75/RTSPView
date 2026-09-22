/* Product workspaces. Editors retain the existing authenticated save handlers. */
const workspace = (() => {
  let telemetry, config, monitorMode = 'wall', signature = '', editor, editing, returnFocus;
  const $ = selector => document.querySelector(selector);
  function node(tag, text, className) {
    const result = document.createElement(tag);
    if (text !== undefined) result.textContent = text;
    if (className) result.className = className;
    return result;
  }
  function button(text, action, className = 'secondary') {
    const result = node('button', text, className); result.type = 'button'; result.onclick = action; return result;
  }
  function init() {
    const monitor = $('#page-overview');
    const metrics=document.createElement('div');metrics.hidden=true;metrics.append($('#stats'),$('.performance'));document.body.append(metrics);
    monitor.replaceChildren();
    const toolbar = node('div', undefined, 'section-title workspace-toolbar');
    const layoutLabel = node('label', 'On wall'); const layouts = node('select'); layouts.id = 'monitorLayout';
    layouts.setAttribute('aria-label', 'Wall layout'); layoutLabel.append(layouts);
    const apply = button('Switch layout', async () => {
      if (!config || layouts.value === config.activeLayoutId) return;
      if (wallDesigner.isDirty()) { $('#monitorNotice').textContent = 'Save or discard layout edits before switching the wall.'; return; }
      apply.disabled = true;
      try {
        const latest = await api('/api/config');
        const result = await api('/api/layouts', {method:'PUT',body:JSON.stringify({layouts:latest.layouts,activeLayoutId:layouts.value,revision:latest.layoutRevision})});
        config = {...latest,...result,layoutRevision:result.revision}; wallDesigner.load({...config,cameras:cameraInventory}); sync();
      } catch (error) { $('#monitorNotice').textContent = error.message; }
      finally { apply.disabled = false; }
    });
    const views = node('div', undefined, 'segmented'); views.setAttribute('role','group'); views.setAttribute('aria-label','Monitor view');
    for (const [id, title] of [['wall','Active wall'],['all','All streams']]) {
      const item = button(title, () => { monitorMode=id; signature=''; sync(); }); item.dataset.monitorMode=id; views.append(item);
    }
    toolbar.append(layoutLabel, apply, views);
    const health = node('div', 'Loading stream health…', 'monitor-health'); health.id='monitorHealth'; health.setAttribute('role','status');
    const notice = node('p', '', 'workspace-notice'); notice.id='monitorNotice'; notice.setAttribute('role','status');
    const board = node('div', undefined, 'monitor-board'); board.id='monitorBoard'; board.setAttribute('aria-label','Snapshot monitor');
    const note=node('p','Snapshot previews · select a stream for details','workspace-caption');
    monitor.append(toolbar,health,notice,board,note);
    const inventory = node('div'); inventory.id='streamInventory'; inventory.setAttribute('role','table'); inventory.setAttribute('aria-label','Streams');
    $('#page-cameras').append(inventory);
    const bank=$('#cameras'); bank.hidden=true; $('#page-cameras').append(bank);
    const search=node('input');search.type='search';search.placeholder='Search streams';search.setAttribute('aria-label','Search streams');search.id='streamSearch';
    search.oninput=filter;$('.camera-page-toolbar').prepend(search);
    editor=node('dialog',undefined,'stream-drawer');editor.setAttribute('aria-labelledby','streamEditorTitle');
    const head=node('div',undefined,'drawer-heading'); const title=node('h2','Stream details');title.id='streamEditorTitle';
    head.append(title,button('Close',()=>editor.close()));editor.append(head);document.body.append(editor);
    editor.addEventListener('close',()=>{if(editor.open)return;const slot=editing?.dataset.slot;if(editing){bank.append(editing);editing=null;}sync();if(returnFocus?.isConnected)returnFocus.focus();else document.querySelector(`.stream-row[data-slot="${slot}"] button`)?.focus();});
    new ResizeObserver(fitMonitor).observe(board);
    // Drafts live in their forms when the drawer closes, and are restored on reopening.
    $('#shareFeedback')?.classList.add('quiet');
  }
  function openStream(slot, trigger) {
    const form=document.querySelector(`.camera-card[data-kind="camera"][data-slot="${slot}"]`);
    if(!form)return;
    if(editor.open)editor.close();
    editing=form;returnFocus=trigger||document.activeElement;$('#streamEditorTitle').textContent=form.querySelector('.slot').textContent;
    editor.append(form);form.querySelector('.camera-settings').open=true;editor.showModal();
    editor.querySelector('.drawer-heading button').focus();
  }
  function reset() { if(editing){$('#cameras').append(editing);editing=null;}if(editor?.open)editor.close(); signature=''; }
  function load(value) {config=value;signature='';sync();}
  function filter() {
    const text=$('#streamSearch').value.toLowerCase();let visible=0;
    for(const row of document.querySelectorAll('.stream-row')) {row.hidden=!row.dataset.name.includes(text);if(!row.hidden)visible++;}
    $('#inventoryEmpty').hidden=visible>0;$('#inventoryEmpty').textContent=cameraInventory.length?'No streams match your search.':'Add your first stream to get started.';
  }
  function state(camera) {
    if(!camera.enabled)return ['Disabled','neutral'];
    if(!camera.rtspUrl)return ['Not configured','neutral'];
    if(!telemetry)return ['Checking…','neutral'];
    if(telemetry.viewerPaused)return ['Viewer paused','neutral'];
    if(!telemetry.viewerConnected)return ['Viewer offline','neutral'];
    const stream=telemetry.viewer?.cameras.find(c=>c.slot===camera.slot);
    if(stream?.frameWarning)return ['Stale video','warning'];
    const name=stream?.state||'Unknown';
    return [({Live:'Connected',StreamError:'Stream error',NotConfigured:'Not configured'})[name]||name,name==='Live'?'healthy':['Connecting','Reconnecting','Buffering'].includes(name)?'warning':'error'];
  }
  function status() {
    for(const item of document.querySelectorAll('[data-stream-status]')){
      const camera=cameraInventory.find(c=>c.slot===Number(item.dataset.streamStatus));if(!camera)continue;
      const [text,tone]=state(camera);item.textContent=text;item.dataset.tone=tone;
    }
    const enabled=cameraInventory.filter(c=>c.enabled&&c.rtspUrl),live=enabled.filter(c=>state(c)[1]==='healthy').length;
    $('#monitorHealth').textContent=!telemetry?'Checking viewer…':telemetry.viewerPaused?'Viewer paused · use Start Viewer in Settings → Maintenance to resume':!telemetry.viewerConnected?'Viewer offline · showing last available snapshots':`${live} of ${enabled.length} streams connected`;
    $('#monitorHealth').dataset.tone=telemetry&&!telemetry.viewerConnected?'warning':'neutral';
  }
  function sync() {
    if(!editor)return;
    const active=wallDesigner.active(); if(config&&active){config.layouts=wallDesigner.savedLayouts();config.activeLayoutId=active.id;}
    const next=JSON.stringify([cameraInventory,active,config?.layouts,monitorMode]);
    if(signature!==next){
      signature=next;
      const list=$('#streamInventory');list.replaceChildren();
      const head=node('div',undefined,'stream-table-head');head.setAttribute('role','row');
      for(const text of ['Stream','Status','On active wall','Actions']){const cell=node('span',text);cell.setAttribute('role','columnheader');head.append(cell);}list.append(head);
      for(const camera of cameraInventory){
        const row=node('div',undefined,'stream-row');row.dataset.name=camera.name.toLowerCase();row.dataset.slot=camera.slot;row.setAttribute('role','row');
        const identity=node('div',undefined,'stream-identity');identity.setAttribute('role','cell');
        const thumb=node('img');thumb.alt='';thumb.className='inventory-thumbnail';
        const name=button(camera.name,()=>openStream(camera.slot,name),'text-button');identity.append(thumb,name);row.append(identity);
        dashboardUX.snapshot(thumb,camera.slot);
        const health=node('span',undefined,'status-label');health.dataset.streamStatus=camera.slot;health.setAttribute('role','cell');
        const placement=node('span',active?.tiles.some(t=>t.cameraSlot===camera.slot)?'Assigned':'Not assigned','muted');placement.setAttribute('role','cell');
        const action=node('div');action.setAttribute('role','cell');const edit=button('Edit',()=>openStream(camera.slot,edit),'text-button');action.append(edit);
        row.append(health,placement,action);list.append(row);
      }
      const empty=node('p','', 'empty-state');empty.id='inventoryEmpty';list.append(empty);filter();
      const picker=$('#monitorLayout'), chosen=picker.value;picker.replaceChildren();
      for(const layout of config?.layouts||[])picker.add(new Option(layout.name,layout.id));picker.value=(config?.layouts||[]).some(l=>l.id===chosen)?chosen:active?.id||'';
      for(const item of document.querySelectorAll('[data-monitor-mode]'))item.setAttribute('aria-pressed',String(item.dataset.monitorMode===monitorMode));
      const board=$('#monitorBoard');board.replaceChildren();board.classList.toggle('all-streams',monitorMode==='all');
      board.style.backgroundColor=monitorMode==='wall'?(active?.backgroundColor||'#000000'):'';
      const outputWidth=active?.outputWidth||(active?.aspectRatio==='9:16'?1080:1920),outputHeight=active?.outputHeight||(active?.aspectRatio==='9:16'?1920:1080);
      board.style.aspectRatio=monitorMode==='wall'?`${outputWidth}/${outputHeight}`:'';
      board.style.setProperty('--monitor-ratio',outputWidth/outputHeight);
      const proportions=active?wallProportions(active):null;
      const tiles=monitorMode==='wall'?(active?.tiles||[]):cameraInventory.map(c=>({cameraSlot:c.slot}));
      for(const tile of tiles){
        if(tile.kind==='weather'){
          const card=node('div',undefined,'monitor-tile');const bounds=proportions.bounds(tile);Object.assign(card.style,{left:bounds.left*100+'%',top:bounds.top*100+'%',width:bounds.width*100+'%',height:bounds.height*100+'%'});card.append(weatherUi.preview(tile.weather));board.append(card);continue;
        }
        const camera=cameraInventory.find(c=>c.slot===tile.cameraSlot);if(!camera)continue;
        const card=button('',()=>showDetails(camera),'monitor-tile');card._tile=tile;
        if(monitorMode==='wall')Object.assign(card.style,{background:active?.backgroundColor||'#000000',borderColor:active?.borderColor||'#24272b',borderWidth:(active?.showTileBorders??config?.showTileBorders??true)?'1px':'0px'});
        card.setAttribute('aria-label','View '+camera.name);card.dataset.cameraSlot=camera.slot;
        if(monitorMode==='wall'){const bounds=proportions.bounds(tile);Object.assign(card.style,{left:bounds.left*100+'%',top:bounds.top*100+'%',width:bounds.width*100+'%',height:bounds.height*100+'%'});}
        const image=node('img');image.alt='';image.className='feed-thumbnail';
        const name=node('span',camera.name,'monitor-name'),health=node('span',undefined,'status-label');health.dataset.streamStatus=camera.slot;
        card.append(image,name,health);board.append(card);image.addEventListener('load',fitMonitor);dashboardUX.snapshot(image,camera.slot);
      }
      if(!tiles.length)board.append(node('p',config?cameraInventory.some(c=>c.rtspUrl)?'Streams configured but unassigned. Open Layouts to place them, then Apply to wall.':'No source configured. Open Streams and add an RTSP address, then assign the stream in Layouts.':'Loading snapshot previews…','empty-state'));
    }
    status();fitMonitor();
  }
  function fitMonitor() {
    const active=wallDesigner.active();if(!active||monitorMode!=='wall')return;
    for(const card of document.querySelectorAll('.monitor-tile')){
      const image=card.querySelector('img'),tile=card._tile;if(!image||!tile)continue;
      const size=telemetry?.viewer?.cameras.find(c=>c.slot===tile.cameraSlot);
      const width=size?.width||image.naturalWidth||16,height=size?.height||image.naturalHeight||9,w=card.clientWidth,h=card.clientHeight;
      const mode=tile.sizing||'fit',scale=mode==='original'&&size?.width?$('#monitorBoard').clientWidth/(active.outputWidth||1920):mode==='fill'?Math.max(w/width,h/height):Math.min(w/width,h/height);
      const zoom=(tile.zoomPercent??100)/100,rw=(mode==='stretch'?w:width*scale)*zoom,rh=(mode==='stretch'?h:height*scale)*zoom;
      Object.assign(image.style,{position:'absolute',width:rw+'px',height:rh+'px',left:(w-rw)*(tile.horizontalPositionPercent??50)/100+'px',top:(h-rh)*(tile.verticalPositionPercent??50)/100+'px'});
    }
  }
  function showDetails(camera) {
    const form=document.querySelector(`.camera-card[data-kind="camera"][data-slot="${camera.slot}"]`);
    if(form)dashboardUX.preview(form);
  }
  function overlay(form) {
    const settings=form.querySelector('.camera-settings');settings.open=true;
    const placement=settings.querySelector('.overlay-placement');
    form.elements.viewportShape.closest('label').hidden=true;
    const upload=placement.querySelector('.custom-viewport-upload');upload.querySelector('label').hidden=true;upload.querySelector('p').hidden=true;
    const shapeName=node('p');shapeName.className='shape-name';
    const updateShape=()=>{shapeName.textContent=form.elements.viewportShape.selectedOptions[0].textContent;};updateShape();form.elements.viewportShape.addEventListener('change',updateShape);
    placement.querySelector('.open-shape-editor').before(shapeName);placement.querySelector('.open-shape-editor').textContent='Edit shape';

    const tabs=node('div',undefined,'inspector-tabs');tabs.setAttribute('role','tablist');tabs.setAttribute('aria-label','Overlay properties');
    const sections=[...placement.querySelectorAll(':scope > details.inspector-section')];
    const connection=[...settings.querySelectorAll(':scope > details')].find(d=>d.querySelector('summary')?.textContent.includes('Connection'));
    if(connection)sections.push(connection);
    const names=['Position','Appearance','Shape','Image','Connection'];
    const select=index=>sections.forEach((section,i)=>{section.hidden=i!==index;section.open=i===index;tabs.children[i].setAttribute('aria-selected',String(i===index));tabs.children[i].tabIndex=i===index?0:-1;});
    sections.forEach((section,i)=>{const tab=button(names[i],()=>select(i));tab.setAttribute('role','tab');const id='overlay-'+form.dataset.slot+'-'+i;section.id=id;section.setAttribute('role','tabpanel');tab.id=id+'-tab';tab.setAttribute('aria-controls',id);section.setAttribute('aria-labelledby',tab.id);tab.onkeydown=e=>{const next={ArrowRight:(i+1)%sections.length,ArrowLeft:(i+sections.length-1)%sections.length,Home:0,End:sections.length-1}[e.key];if(next!==undefined){e.preventDefault();select(next);tabs.children[next].focus();}};tabs.append(tab);});
    settings.prepend(tabs);select(0);
    const info=form.querySelector('.overlay-automation-info');
    if(info){const details=node('details');details.append(node('summary','Automation'));info.before(details);details.append(info);}
    form.addEventListener('invalid',event=>{const i=sections.findIndex(s=>s.contains(event.target));if(i>=0)select(i);},true);
  }
  return {init,load,sync,reset,openStream,overlay,telemetry(value){telemetry=value;status();fitMonitor();},unavailable(){telemetry=null;status();$('#monitorHealth').textContent='Connection to controller lost · status may be out of date';}};
})();
