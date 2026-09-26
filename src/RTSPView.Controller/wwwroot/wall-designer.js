// getRandomValues is available on ordinary LAN HTTP; randomUUID requires a secure context.
function layoutItemId() {
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('');
}

function createWallDesigner(isAutomation = false) {
  let config, saved, draft, selectedId, selectedTile = -1, previous, dirty = false, busy = false;
  let root, board, status, observer, armedCamera = null;
  const redoHistory=[];
  window.addEventListener('weather-overlay-saved',event=>{if(!config)return;config.weatherOverlays=[...(config.weatherOverlays||[]).filter(o=>o.hostCameraSlot!==event.detail.hostCameraSlot),event.detail];render();});
  window.addEventListener('aircraft-overlay-saved',event=>{if(!config)return;config.aircraftOverlays=[...(config.aircraftOverlays||[]).filter(o=>o.hostCameraSlot!==event.detail.hostCameraSlot),event.detail];render();});
  function fitPreview(){
    if(!board||!root?.getClientRects().length)return;
    const stage=board.parentElement,foot=root.querySelector('.designer-preview-foot');
    const padding=parseFloat(getComputedStyle(stage).paddingTop)+parseFloat(getComputedStyle(stage).paddingBottom);
    const available=Math.max(120,window.innerHeight-(stage.getBoundingClientRect().top+window.scrollY)-padding-(foot?.offsetHeight||45)-45);
    const [w,h]=resolution(current());board.style.maxWidth=(available*w/h)+'px';
  }
  window.addEventListener('resize',fitPreview);
  const resolution = l => [l.outputWidth || (l.aspectRatio === '9:16' ? 1080 : 1920), l.outputHeight || (l.aspectRatio === '9:16' ? 1920 : 1080)];
  let streamDimensions = new Map(), panImage = false;
  function sizeImages(candidate=null, candidateIndex=-1) {
    if(!board)return;
    const [width]=resolution(current()),scale=board.clientWidth/width;
    board.querySelectorAll('img[data-tile-index]').forEach(image=>{
      const index=Number(image.dataset.tileIndex),tile=index===candidateIndex?candidate:current().tiles[index],slot=tile.cameraSlot>0?tile.cameraSlot:previewCameras.get(current().id+":"+tile.cameraSlot),size=streamDimensions.get(slot);
      const w=image.parentElement.clientWidth,h=image.parentElement.clientHeight;
      const sw=size?.width||image.naturalWidth||16,sh=size?.height||image.naturalHeight||9,mode=tile.sizing||'fit';
      const fit=mode==='original'&&size?scale:mode==='fill'?Math.max(w/sw,h/sh):Math.min(w/sw,h/sh);
      const zoom=(tile.zoomPercent??100)/100,rw=(mode==='stretch'?w:sw*fit)*zoom,rh=(mode==='stretch'?h:sh*fit)*zoom;
      Object.assign(image.style,{width:rw+'px',height:rh+'px',left:(w-rw)*(tile.horizontalPositionPercent??50)/100+'px',top:(h-rh)*(tile.verticalPositionPercent??50)/100+'px',objectFit:'fill'});
    });
    board.querySelectorAll('.designer-weather-preview').forEach(host=>{const bounds=wallProportions(current()).bounds(current().tiles[Number(host.dataset.weatherIndex)]),[w,h]=resolution(current());const card=host.firstElementChild;Object.assign(card.style,{width:w*bounds.width+'px',height:h*bounds.height+'px',transformOrigin:'top left',transform:'scale('+host.clientWidth/(w*bounds.width)+')'});});
    board.querySelectorAll('.designer-aircraft-preview').forEach(host=>{const bounds=wallProportions(current()).bounds(current().tiles[Number(host.dataset.aircraftIndex)]),[w,h]=resolution(current());const card=host.firstElementChild;Object.assign(card.style,{width:w*bounds.width+'px',height:h*bounds.height+'px',transformOrigin:'top left',transform:'scale('+host.clientWidth/(w*bounds.width)+')'});});
    board.querySelectorAll('.designer-weather-overlay').forEach(node=>{const overlay=config.weatherOverlays.find(o=>o.hostCameraSlot===Number(node.dataset.weatherSlot));const host=node.parentElement;const b=weatherUi.overlayBounds(overlay,host.clientWidth/scale,host.clientHeight/scale);Object.assign(node.style,Object.fromEntries(Object.entries(b).map(([key,value])=>[key,value*scale+'px'])));});
    board.querySelectorAll('.designer-aircraft-overlay[data-aircraft-slot]').forEach(node=>{const overlay=(config.aircraftOverlays||[]).find(o=>o.hostCameraSlot===Number(node.dataset.aircraftSlot));if(!overlay)return;const host=node.parentElement;const b=aircraftUi.overlayBounds(overlay,host.clientWidth/scale,host.clientHeight/scale);Object.assign(node.style,Object.fromEntries(Object.entries(b).map(([key,value])=>[key,value*scale+'px'])));});
    const note=root.querySelector('[data-dimensions-note]');if(note)note.hidden=streamDimensions.has(Number(note.dataset.dimensionsNote));
  }

  const streamAspects = new Map(), previewCameras = new Map(), undoHistory = [];
  let checkpoint, drawer = '', searchText = '';
  function toggleDrawer(value) { drawer = drawer === value ? '' : value; render(); }
  const copy = value => JSON.parse(JSON.stringify(value));
  const current = () => draft.layouts.find(layout => layout.id === selectedId);
  const el = (tag, text, className) => {
    const node = document.createElement(tag);
    if (text !== undefined) node.textContent = text;
    if (className) node.className = className;
    return node;
  };
  function button(label, action, parent, secondary = true) {
    const node = el('button', label, secondary ? 'secondary' : ''); node.type = 'button';
    node.onclick = action; parent.append(node); return node;
  }
  function field(label, input, parent) {
    const wrapper = el('label', label); input.setAttribute('aria-label',label);wrapper.append(input); parent.append(wrapper); return input;
  }
  function message(text) { status.textContent = text; }
  function changed() {
    const layout=current(),small=layout.tiles.filter(t=>t.rowSpan===1&&t.columnSpan===1);
    for(const [key,position] of [['rowWeights','row'],['columnWeights','column']])if(layout[key]?.length){
      const linked=[...new Set(small.map(t=>t[position]))];
      if(linked.length){const mean=linked.reduce((n,i)=>n+layout[key][i],0)/linked.length;for(const i of linked)layout[key][i]=mean;}
    }
    if(checkpoint){undoHistory.push(checkpoint);if(undoHistory.length>50)undoHistory.shift();}
    redoHistory.length=0;dirty = true; render(); message(isAutomation ? 'Unsaved automation layout. Save when ready.' : 'Unsaved draft. Apply when ready to change the wall.');
  }
  function validTile(candidate, index) {
    const layout = current();
    return [candidate.row,candidate.column,candidate.rowSpan,candidate.columnSpan].every(Number.isInteger) &&
      candidate.row >= 0 && candidate.column >= 0 && candidate.rowSpan >= 1 && candidate.columnSpan >= 1 &&
      candidate.row + candidate.rowSpan <= layout.rows && candidate.column + candidate.columnSpan <= layout.columns &&
      !layout.tiles.some((tile,i) => i !== index && ((["weather","aircraft"].includes(tile.kind) || ["weather","aircraft"].includes(candidate.kind) ? tile.itemId && tile.itemId === candidate.itemId : tile.cameraSlot === candidate.cameraSlot) ||
        candidate.row < tile.row+tile.rowSpan && candidate.row+candidate.rowSpan > tile.row &&
        candidate.column < tile.column+tile.columnSpan && candidate.column+candidate.columnSpan > tile.column));
  }
  function updateTile(candidate, index) {
    const layout=current(), original=layout.tiles[index];
    // A filled preset should be rearrangeable without first removing a camera.
    const swapIndex=layout.tiles.findIndex((tile,i)=>i!==index&&tile.row===candidate.row&&tile.column===candidate.column&&
      tile.rowSpan===candidate.rowSpan&&tile.columnSpan===candidate.columnSpan);
    if(swapIndex>=0&&candidate.cameraSlot===original.cameraSlot&&candidate.rowSpan===original.rowSpan&&candidate.columnSpan===original.columnSpan){
      const other=layout.tiles[swapIndex];
      layout.tiles[index]=candidate;layout.tiles[swapIndex]={...other,row:original.row,column:original.column};
      if(validTile(candidate,index)&&validTile(layout.tiles[swapIndex],swapIndex)){changed();return;}
      layout.tiles[index]=original;layout.tiles[swapIndex]=other;
    }
    if (!validTile(candidate,index)) { render(); message('That tile overlaps another stream or extends beyond the grid.'); return; }
    if(isAutomation&&candidate.cameraSlot!==original.cameraSlot)layout.focusSlots=layout.focusSlots.map(slot=>slot===original.cameraSlot?candidate.cameraSlot:slot);
    current().tiles[index] = candidate; changed();
  }
  function preset(id) {
    const layout=current();
    const slots=config.cameras.map(camera=>camera.slot);if(isAutomation)slots.unshift(...(id==='dual'?[-1,-2]:[-1]));
    layout.rowWeights=[];layout.columnWeights=[];
    Object.assign(layout,wallLayoutPresets.create(id,layout.aspectRatio||'16:9',slots));
    if(isAutomation)layout.focusSlots=layout.tiles.filter(t=>t.cameraSlot<0).map(t=>t.cameraSlot);
    selectedTile=-1;changed();
  }
  function newLayout() {
    if (draft.layouts.length >= 32) { message('You can save up to 32 layouts.'); return; }
    const dialog = el('dialog', undefined, 'new-layout-dialog'), form = el('form'); dialog.append(form);
    form.append(el('h2', 'New ' + (isAutomation ? 'automation' : 'standard') + ' layout'));
    const name = el('input'); name.required = true; name.maxLength = 80; name.value = 'New layout'; field('Layout name', name, form);
    const start = el('select'); start.add(new Option(isAutomation ? 'Blank canvas with one focus tile' : 'Blank canvas', 'blank')); wallLayoutPresets.catalog.forEach(p => start.add(new Option(p.name, p.id))); field('Start from', start, form);
    const buttons = el('div', undefined, 'control-buttons'); form.append(buttons);
    const create = el('button', 'Create layout'); create.type = 'submit'; buttons.append(create); button('Cancel', () => dialog.close(), buttons);
    dialog.onclose = () => dialog.remove();
    form.onsubmit = e => {
      e.preventDefault(); if (!name.value.trim()) { name.setCustomValidity('Enter a layout name.'); name.reportValidity(); return; }
      const item = {id: 'layout-' + layoutItemId(), name: name.value.trim(), rows: 3, columns: 3, aspectRatio: '16:9', outputWidth: 1920, outputHeight: 1080, rowWeights: [], columnWeights: [], tiles: [], focusSlots: []};
      if (start.value === 'blank') { if (isAutomation) { item.tiles = [{cameraSlot: -1, row: 0, column: 0, rowSpan: 1, columnSpan: 1, sizing: 'fit'}]; item.focusSlots = [-1]; } }
      else { const slots = config.cameras.map(c => c.slot); if (isAutomation) slots.unshift(...(start.value === 'dual' ? [-1, -2] : [-1])); Object.assign(item, wallLayoutPresets.create(start.value, '16:9', slots)); if (isAutomation) item.focusSlots = item.tiles.filter(t => t.cameraSlot < 0).map(t => t.cameraSlot); }
      dialog.close(); draft.layouts.push(item); selectedId = item.id; selectedTile = -1; drawer = 'add'; changed();
    };
    name.oninput = () => name.setCustomValidity(''); root.append(dialog); dialog.showModal(); name.focus(); name.select();
  }
  function addCamera(slot, row, column, rowSpan=1, columnSpan=1) {
    if(current().tiles.length>=16){message('A layout supports up to 16 tiles. Remove a tile first.');return;}
    const tile = {cameraSlot:slot,row,column,rowSpan,columnSpan,sizing:"fit"};
    if (!validTile(tile,-1)) { message('Choose an empty cell and a stream not already in this layout.'); return; }
    armedCamera=null;current().tiles.push(tile); drawer='tile'; selectedTile = current().tiles.length-1; changed();
  }
  async function persist(apply = false, revert = false) {
    if (busy) return;
    const payload = revert ? copy(previous) : copy(draft);if(revert)payload.revision=saved.revision;
    if (apply) payload.activeLayoutId = selectedId;
    // Saving edits to the active layout also changes its appearance. Require Apply for that case.
    if (!isAutomation && !apply && !revert && JSON.stringify(payload.layouts.find(l=>l.id===saved.activeLayoutId)) !==
        JSON.stringify(saved.layouts.find(l=>l.id===saved.activeLayoutId))) {
      message('This draft edits the live layout. Use Apply, or duplicate it to save a separate layout.'); return;
    }
    busy = true; root.inert = true; message('Saving…');
    try {
      const result = await api(isAutomation?'/api/automation/layouts':'/api/layouts',{method:'PUT',body:JSON.stringify(payload)});
      previous = copy(saved); saved = copy(result); draft = copy(result); dirty = false;
      undoHistory.length=0;redoHistory.length=0;
      if (!draft.layouts.some(layout=>layout.id===selectedId)) selectedId = draft.activeLayoutId;
      render(); dashboardUX.sync(); message(revert ? 'Previous saved layouts restored.' : apply ? 'Layout applied. The viewer will update shortly.' : 'Layouts saved.');
      if(isAutomation){message('Automation layouts saved. Choose one in a focused-layout rule.');await automationUi.refreshLayouts();tapoUi.updateTargets({automationViewLayouts: result.layouts});}
      else tapoUi.updateTargets({layouts: result.layouts});
    } catch(error) { message(error.message);requestAnimationFrame(()=>status.focus()); }
    finally { busy = false; root.inert = false; }
  }
  function render() {
    const focus=document.activeElement,focusLabel=root.contains(focus)?focus.getAttribute('aria-label'):null,openSections=[...root.querySelectorAll('details[open]')].map(n=>n.querySelector('summary')?.textContent);
    requestAnimationFrame(()=>{for(const details of root.querySelectorAll('details'))if(openSections.includes(details.querySelector('summary')?.textContent))details.open=true;if(!focusLabel)return;const next=[...root.querySelectorAll('[aria-label]')].find(n=>n.getAttribute('aria-label')===focusLabel);next?.focus({preventScroll:true});});
    checkpoint=copy({draft,selectedId,selectedTile});
    observer?.disconnect();root.replaceChildren();
    const layout=current(), proportions=wallProportions(layout);
    const [outputWidth,outputHeight]=resolution(layout);
    const top=el('div',undefined,'designer-toolbar');root.append(top);
    const picker=el('div',undefined,'designer-layout-picker');top.append(picker);
    const layouts=el('select');
    for(const item of draft.layouts)layouts.add(new Option(item.name,item.id));
    layouts.value=selectedId;layouts.onchange=()=>{selectedId=layouts.value;selectedTile=-1;render();};
    field('Saved layout',layouts,picker);
    const layoutTools=el('div',undefined,'designer-layout-tools');picker.append(layoutTools);
    const badge=el('span',dirty?'Unsaved changes':!isAutomation&&selectedId===saved.activeLayoutId?'On wall':'Saved','designer-badge'+(dirty?' draft':''));layoutTools.append(badge);
    button('New layout', newLayout, layoutTools);
    const manage=el('details',undefined,'designer-menu');const manageSummary=el('summary','Manage layout');manageSummary.setAttribute('aria-label','Manage layout');manageSummary.title='Manage layout';manage.append(manageSummary);layoutTools.append(manage);
    const menu=el('div',undefined,'designer-popover');manage.append(menu);
    const name=el('input');name.value=layout.name;name.maxLength=80;
    name.oninput=()=>{layout.name=name.value.trim();dirty=true;badge.textContent='Unsaved changes';badge.classList.add('draft');root.querySelector('.designer-discard').disabled=false;layouts.selectedOptions[0].textContent=layout.name;message('Unsaved changes.');};field('Layout name',name,menu);
    button('Duplicate layout',()=>{
      if(draft.layouts.length>=32){message('You can save up to 32 layouts.');return;}
      const item=copy(layout);item.id='layout-'+Date.now().toString(36)+'-'+Math.random().toString(36).slice(2,8);
      item.name=(item.name+' copy').slice(0,80);draft.layouts.push(item);selectedId=item.id;changed();
    },menu);
    const deleteButton=button('Delete layout',async()=>{
      if(!await uiDialogs.ask('Delete layout “'+layout.name+'”? Save or apply the layout collection to make this removal permanent.',{accept:'Delete layout'}))return;
      draft.layouts=draft.layouts.filter(item=>item.id!==selectedId);selectedId=isAutomation?draft.layouts[0].id:draft.activeLayoutId;selectedTile=-1;changed();
    },menu);deleteButton.disabled=isAutomation?draft.layouts.length===1:selectedId===saved.activeLayoutId;deleteButton.classList.add('designer-danger');
    const revert=button('Revert last save',()=>persist(false,true),menu);revert.disabled=!previous;
    const actions=el('div',undefined,'designer-actions');top.append(actions);
    if(!isAutomation&&pluginsUi.enabled('weather'))button('Add weather',()=>{
      let place;for(let row=0;row<layout.rows&&!place&&layout.tiles.length<16;row++)for(let column=0;column<layout.columns;column++){
        const candidate={kind:'weather',itemId:layoutItemId(),cameraSlot:0,row,column,rowSpan:1,columnSpan:1,sizing:'fit'};if(validTile(candidate,-1)){place=candidate;break;}
      }
      if(!place){
        const dialog=el('dialog',undefined,'weather-choice');dialog.append(el('h2','Add weather'),el('p','This grid is full. Choose a camera, then add an overlay or replace its tile.'));
        const choices=el('select');layout.tiles.forEach((t,i)=>{if(!['weather','aircraft'].includes(t.kind))choices.add(new Option(config.cameras.find(c=>c.slot===t.cameraSlot)?.name||'Stream',i));});if(selectedTile>=0)choices.value=String(selectedTile);if(!choices.value&&choices.options.length)choices.selectedIndex=0;field('Camera tile',choices,dialog);
        const addOverlayButton=button('Add Weather Widget',()=>{const t=layout.tiles[Number(choices.value)];dialog.close();weatherUi.overlayEditor(t.cameraSlot);},dialog);choices.onchange=()=>{const slot=layout.tiles[Number(choices.value)]?.cameraSlot;addOverlayButton.disabled=!slot||slot>32;};choices.onchange();
        button('Replace tile with weather',()=>{const index=Number(choices.value);dialog.close();weatherUi.editor(weatherUi.defaults(),weather=>{layout.tiles[index]={...layout.tiles[index],kind:'weather',itemId:layoutItemId(),cameraSlot:0,aircraft:null,weather};selectedTile=index;drawer='tile';changed();});},dialog).disabled=!choices.options.length;
        button('Cancel',()=>dialog.close(),dialog);dialog.onclose=()=>dialog.remove();document.body.append(dialog);dialog.showModal();return;
      }
      weatherUi.editor(weatherUi.defaults(),weather=>{layout.tiles.push({...place,weather});selectedTile=layout.tiles.length-1;drawer='tile';changed();});
    },actions);
    if(!isAutomation&&pluginsUi.enabled('aircraft'))button('Add aircraft',()=>{
      let place;for(let row=0;row<layout.rows&&!place&&layout.tiles.length<16;row++)for(let column=0;column<layout.columns;column++){
        const candidate={kind:'aircraft',itemId:layoutItemId(),cameraSlot:0,row,column,rowSpan:1,columnSpan:1,sizing:'fit'};if(validTile(candidate,-1)){place=candidate;break;}
      }
      if(!place){
        const dialog=el('dialog',undefined,'aircraft-choice');dialog.append(el('h2','Add aircraft'),el('p','This grid is full. Choose a camera, then add an overlay or replace its tile.'));
        const choices=el('select');layout.tiles.forEach((t,i)=>{if(!['weather','aircraft'].includes(t.kind))choices.add(new Option(config.cameras.find(c=>c.slot===t.cameraSlot)?.name||'Stream',i));});if(selectedTile>=0)choices.value=String(selectedTile);if(!choices.value&&choices.options.length)choices.selectedIndex=0;field('Camera tile',choices,dialog);
        const addOverlayButton=button('Add Aircraft Widget',()=>{const t=layout.tiles[Number(choices.value)];dialog.close();aircraftUi.overlayEditor(t.cameraSlot);},dialog);choices.onchange=()=>{const slot=layout.tiles[Number(choices.value)]?.cameraSlot;addOverlayButton.disabled=!slot||slot>32;};choices.onchange();
        button('Appear over camera when nearby',()=>{const index=Number(choices.value);dialog.close();aircraftUi.editor(aircraftUi.defaults(),aircraft=>{layout.tiles[index]={...layout.tiles[index],aircraft};selectedTile=index;drawer='tile';changed();});},dialog).disabled=!choices.options.length;
        button('Permanent aircraft tile',()=>{const index=Number(choices.value);dialog.close();aircraftUi.editor(aircraftUi.defaults(),aircraft=>{layout.tiles[index]={...layout.tiles[index],kind:'aircraft',itemId:layoutItemId(),cameraSlot:0,weather:null,aircraft};selectedTile=index;drawer='tile';changed();});},dialog).disabled=!choices.options.length;
        button('Cancel',()=>dialog.close(),dialog);dialog.onclose=()=>dialog.remove();document.body.append(dialog);dialog.showModal();return;
      }
      aircraftUi.editor(aircraftUi.defaults(),aircraft=>{layout.tiles.push({...place,aircraft});selectedTile=layout.tiles.length-1;drawer='tile';changed();});
    },actions);
    for(const [id,label] of [['add','Add stream'],['presets','Presets'],['sizing','Sizing'],['advanced','Canvas settings'],['help','Help']]){const control=button(label,()=>toggleDrawer(id),actions);control.setAttribute('aria-expanded',String(drawer===id));}

    button('Undo',()=>{const state=undoHistory.pop();if(!state)return;redoHistory.push(copy({draft,selectedId,selectedTile}));draft=state.draft;selectedId=state.selectedId;selectedTile=state.selectedTile;dirty=JSON.stringify(draft)!==JSON.stringify(saved);render();message('Last layout edit undone.');},actions).disabled=!undoHistory.length;
    button('Redo',()=>{const state=redoHistory.pop();if(!state)return;undoHistory.push(copy({draft,selectedId,selectedTile}));({draft,selectedId,selectedTile}=state);dirty=JSON.stringify(draft)!==JSON.stringify(saved);render();},actions).disabled=!redoHistory.length;
    button('Discard changes',()=>{draft=copy(saved);selectedId=saved.activeLayoutId;selectedTile=-1;dirty=false;undoHistory.length=0;redoHistory.length=0;render();message('Draft discarded.');},menu).classList.add('designer-discard');root.querySelector('.designer-discard').disabled=!dirty;
    if(isAutomation)button('Save automation layouts',()=>persist(),actions,false);
    else {if(selectedId!==saved.activeLayoutId)button('Save layout',()=>persist(),actions);button('Apply to wall',()=>persist(true),actions,false);}
    for(const [caption,labels] of [
      ['Add content',['Add weather','Add aircraft','Add stream']],
      ['Editing',['Presets','Sizing','Canvas settings','Help','Undo','Redo']],
      ['Wall action',['Save layout','Apply to wall','Save automation layouts']]
    ]) {
      const group=el('div',undefined,'designer-action-group');
      group.setAttribute('role','group');group.setAttribute('aria-label',caption);
      group.append(el('span',caption,'designer-action-label'));
      for(const control of [...actions.children])if(labels.includes(control.textContent))group.append(control);
      if(group.children.length>1)actions.append(group);
    }
    const workspace=el('div',undefined,'designer-workspace'+(drawer?' has-drawer':''));root.append(workspace);
    const preview=el('section',undefined,'designer-preview');workspace.append(preview);
    const previewHead=el('div',undefined,'designer-preview-head');preview.append(previewHead);
    const title=el('div');title.append(el('h2','Wall preview'),el('span',layout.rows+' × '+layout.columns+' grid · '+layout.tiles.length+' items','designer-meta'));previewHead.append(title);
    const gridSettings=el('details',undefined,'designer-settings');gridSettings.append(el('summary','Grid settings'));gridSettings.open=true;
    const canvasControls=el('div',undefined,'designer-canvas-controls');gridSettings.append(canvasControls);
    const dragMode=el('select');dragMode.add(new Option('Move tiles','tiles'));dragMode.add(new Option('Reposition images','images'));dragMode.value=panImage?'images':'tiles';dragMode.onchange=()=>{panImage=dragMode.value==='images';board.classList.toggle('pan-images',panImage);};field('Drag action',dragMode,canvasControls);
    const format=el('select');format.add(new Option('Landscape','16:9'));format.add(new Option('Portrait','9:16'));format.value=layout.aspectRatio||'16:9';
    format.onchange=()=>{Object.assign(layout,wallLayoutPresets.transpose(layout));layout.aspectRatio=format.value;layout.outputWidth=outputHeight;layout.outputHeight=outputWidth;changed();};field('Orientation',format,canvasControls);
    const sizes=el('select');
    for(const [w,h] of [[1280,720],[1920,1080],[2560,1440],[3840,2160],[1920,1200],[3440,1440],[1080,1920]])sizes.add(new Option(w+' × '+h,w+'x'+h));
    sizes.add(new Option('Custom','custom'));sizes.value=[...sizes.options].some(o=>o.value===outputWidth+'x'+outputHeight)?outputWidth+'x'+outputHeight:'custom';
    sizes.onchange=()=>{if(sizes.value==='custom'){custom.hidden=false;return;}[layout.outputWidth,layout.outputHeight]=sizes.value.split('x').map(Number);layout.aspectRatio=layout.outputHeight>layout.outputWidth?'9:16':'16:9';changed();};field('Output resolution',sizes,canvasControls);
    const custom=el('div',undefined,'designer-grid-fields');custom.hidden=sizes.value!=='custom';canvasControls.append(custom);
    const width=el('input'),height=el('input');
    for(const [input,label,value] of [[width,'Pixels wide',outputWidth],[height,'Pixels high',outputHeight]]){input.type='number';input.min=240;input.max=16384;input.value=value;field(label,input,custom);}
    button('Set size',()=>{const w=Number(width.value),h=Number(height.value);if(![w,h].every(n=>Number.isInteger(n)&&n>=240&&n<=16384)){message('Use dimensions from 240 to 16384 pixels.');return;}layout.outputWidth=w;layout.outputHeight=h;layout.aspectRatio=h>w?'9:16':'16:9';changed();},custom);
    const gridFields=el('div',undefined,'designer-grid-fields');canvasControls.append(gridFields);
    const appearance=el('div',undefined,'designer-canvas-appearance');canvasControls.append(appearance);
    const borderless=el('input');borderless.type='checkbox';borderless.checked=!(layout.showTileBorders??config.showTileBorders??true);
    borderless.onchange=()=>{layout.showTileBorders=!borderless.checked;changed();};field('Borderless view',borderless,appearance);
    for(const [key,label,fallback] of [['borderColor','Border color','#24272b'],['backgroundColor','Background color','#000000']]){
      const color=el('input');color.type='color';color.value=layout[key]||fallback;color.disabled=key==='borderColor'&&borderless.checked;
      color.onchange=()=>{layout[key]=color.value;changed();};field(label,color,appearance);
    }
    button('Use display border setting',()=>{layout.showTileBorders=null;changed();},appearance);
    appearance.append(el('p','Background fills empty canvas space and letterboxing. Black bars encoded into a camera image are part of the video.','designer-help'));
    for(const [key,label] of [['rows','Rows'],['columns','Columns']]){
      const input=el('input');input.type='number';input.min=1;input.max=12;input.value=layout[key];
      input.onchange=()=>{
        const value=Number(input.value),old=layout[key];layout[key]=value;
        if(!Number.isInteger(value)||value<1||value>(12)||layout.tiles.some((t,i)=>!validTile(t,i))){layout[key]=old;render();message('Remove or resize tiles before shrinking the grid.');return;}
        layout.rowWeights=[];layout.columnWeights=[];changed();
      };field(label,input,gridFields);
    }
    const presets=el('details',undefined,'designer-preset-library');presets.open=true;
    presets.append(el('summary','Layout presets'));
    const gallery=el('div',undefined,'designer-presets');gallery.setAttribute('aria-label','Layout presets');presets.append(gallery);
    gallery.append(el('p','Choose a starting point. This replaces the draft arrangement.','designer-help'));
    for(const item of wallLayoutPresets.catalog){
      const template=wallLayoutPresets.get(item.id,layout.aspectRatio||'16:9');
      const presetButton=button('',()=>preset(item.id),gallery);presetButton.setAttribute('aria-label','Use '+item.name+' preset');
      const icon=el('span',undefined,'designer-preset-icon');icon.setAttribute('aria-hidden','true');
      icon.style.gridTemplateRows='repeat('+template.rows+',1fr)';icon.style.gridTemplateColumns='repeat('+template.columns+',1fr)';icon.style.aspectRatio=(layout.aspectRatio||'16:9').replace(':','/');
      for(const tile of template.tiles){const cell=el('i');cell.style.gridArea=(tile.row+1)+' / '+(tile.column+1)+' / span '+tile.rowSpan+' / span '+tile.columnSpan;icon.append(cell);}
      presetButton.append(icon,el('span',item.name),el('small',template.tiles.length+(template.tiles.length===1?' stream':' streams')));
    }
    const stage=el('div',undefined,'designer-stage');preview.append(stage);
    board=el('div',undefined,'designer-board');board.setAttribute('aria-label','Stream wall layout preview');
    board.style.backgroundColor=layout.backgroundColor||'#000000';
    board.style.setProperty('--canvas-background',layout.backgroundColor||'#000000');
    board.style.setProperty('--canvas-border',layout.borderColor||'#24272b');
    board.style.setProperty('--canvas-border-width',(layout.showTileBorders??config.showTileBorders??true)?'1px':'0px');
    board.classList.toggle('pan-images',panImage);board.style.backgroundImage='none';board.style.setProperty('--aspect',outputWidth+'/'+outputHeight);board.style.setProperty('--ratio',outputWidth/outputHeight);board.style.setProperty('--rows',layout.rows);board.style.setProperty('--columns',layout.columns);stage.append(board);
    board.ondragover=e=>e.preventDefault();board.ondrop=e=>{e.preventDefault();const slot=Number(e.dataTransfer.getData('text/plain'));if(!config.cameras.some(c=>c.slot===slot))return;const bounds=board.getBoundingClientRect();addCamera(slot,proportions.cell(proportions.rows,(e.clientY-bounds.top)/bounds.height),proportions.cell(proportions.columns,(e.clientX-bounds.left)/bounds.width));};
    const cellAt=e=>{const b=board.getBoundingClientRect();return {row:Math.max(0,Math.min(layout.rows-1,proportions.cell(proportions.rows,(e.clientY-b.top)/b.height))),column:Math.max(0,Math.min(layout.columns-1,proportions.cell(proportions.columns,(e.clientX-b.left)/b.width)))};};
    board.onpointerdown=e=>{
      if(e.target!==board||e.button!==0)return;
      if(armedCamera===null){selectedTile=-1;render();message('Choose an available camera, then click or drag across empty cells.');return;}
      e.preventDefault();const start=cellAt(e);let end=start;
      const ghost=el('div',undefined,'designer-placement');board.append(ghost);board.setPointerCapture(e.pointerId);
      const region=()=>({cameraSlot:armedCamera,row:Math.min(start.row,end.row),column:Math.min(start.column,end.column),rowSpan:Math.abs(end.row-start.row)+1,columnSpan:Math.abs(end.column-start.column)+1});
      const paint=()=>{const t=region();Object.assign(ghost.style,Object.fromEntries(Object.entries(proportions.bounds(t)).map(([k,v])=>[k,v*100+'%'])));ghost.classList.toggle('invalid',!validTile(t,-1));};paint();
      board.onpointermove=m=>{end=cellAt(m);paint();};
      const stop=()=>{board.onpointermove=null;board.onpointerup=null;board.onpointercancel=null;ghost.remove();};
      board.onpointerup=()=>{const t=region();stop();addCamera(t.cameraSlot,t.row,t.column,t.rowSpan,t.columnSpan);};board.onpointercancel=stop;
    };
    layout.tiles.forEach((tile,index)=>{if(['weather','aircraft'].includes(tile.kind)&&!pluginsUi.enabled(tile.kind))return;
      const camera=config.cameras.find(camera=>camera.slot===tile.cameraSlot)||{name:tile.kind==='aircraft'?'Aircraft · '+tile.aircraft.location:tile.kind==='weather'?'Weather · '+tile.weather.location:tile.cameraSlot<0?'Focus '+(-tile.cameraSlot):'Stream',slot:tile.cameraSlot};
      const node=el('div',undefined,'designer-tile'+(index===selectedTile?' selected':''));node.tabIndex=0;
      node.setAttribute('aria-label',camera.name+'; row '+(tile.row+1)+', column '+(tile.column+1));
      const rect=proportions.bounds(tile);Object.assign(node.style,Object.fromEntries(Object.entries(rect).map(([key,value])=>[key,value*100+'%'])));
      const previewSlot=tile.cameraSlot>0?tile.cameraSlot:previewCameras.get(layout.id+':'+tile.cameraSlot);
      const image=el('img');image.alt='';image.draggable=false;if(previewSlot)dashboardUX.snapshot(image,previewSlot);else image.hidden=true;image.onerror=()=>image.style.visibility='hidden';node.append(image);if(!['weather','aircraft'].includes(tile.kind))image.dataset.tileIndex=index;image.addEventListener('load',sizeImages);
      if(tile.kind==='weather'){image.hidden=true;const host=el('div',undefined,'designer-weather-preview');host.dataset.weatherIndex=index;host.append(weatherUi.preview(tile.weather,true));node.append(host);}
      if(pluginsUi.enabled('aircraft')&&!['weather','aircraft'].includes(tile.kind)&&tile.aircraft){const badge=el('span','Aircraft when nearby','designer-aircraft-overlay');badge.style.inset='30% 10%';node.append(badge);}
      if(tile.kind==='aircraft'){image.hidden=true;const host=el('div',undefined,'designer-aircraft-preview');host.dataset.aircraftIndex=index;host.append(aircraftUi.preview(tile.aircraft,true));node.append(host);}
      const guide=el('span','','designer-aspect-guide');guide.hidden=['weather','aircraft'].includes(tile.kind)||index!==selectedTile;node.append(guide);
      const updateGuide=r=>{const ratio=streamAspects.get(previewSlot);guide.textContent=ratio?'Picture fit '+Math.round(Math.min(r.width/r.height*(outputWidth/outputHeight)/ratio,ratio/(r.width/r.height*(outputWidth/outputHeight)))*100)+'%':'Load a preview for sizing';};
      image.onload=()=>{if(image.naturalWidth&&image.naturalHeight){streamAspects.set(previewSlot,image.naturalWidth/image.naturalHeight);image.style.visibility='';updateGuide(rect);}};updateGuide(rect);
      node.append(el('span',camera.name,'designer-caption'));
      const weatherOverlay=(config.weatherOverlays||[]).find(o=>o.enabled&&o.hostCameraSlot===tile.cameraSlot);
      if(pluginsUi.enabled('weather')&&weatherOverlay){const placeholder=el('div','Weather Widget','designer-weather-overlay');placeholder.dataset.weatherSlot=tile.cameraSlot;placeholder.title='Weather enabled · '+weatherOverlay.weather.location;node.append(placeholder);}
      const aircraftOverlay=(config.aircraftOverlays||[]).find(o=>o.enabled&&o.hostCameraSlot===tile.cameraSlot);
      if(pluginsUi.enabled('aircraft')&&aircraftOverlay){const placeholder=el('div','Aircraft Widget','designer-aircraft-overlay');placeholder.dataset.aircraftSlot=tile.cameraSlot;placeholder.title='Aircraft enabled · '+aircraftOverlay.aircraft.location;node.append(placeholder);}

      if(isAutomation&&layout.focusSlots.includes(tile.cameraSlot)){
        node.append(el('span','Chosen by automation','designer-overlay'));
        const previewButton=button('Preview camera',()=>{selectedTile=index;drawer='tile';render();},node);previewButton.classList.add('designer-preview-camera');previewButton.onpointerdown=e=>e.stopPropagation();
      }
      const overlays=[config.doorbellOverlay,config.garageOverlay,...(config.additionalOverlays||[])].map(overlay=>[overlay.camera.name,overlay]).filter(([,o])=>o.camera.enabled&&o.hostCameraSlot===tile.cameraSlot);
      if(overlays.length)node.append(el('span',overlays.map(([name])=>name+' overlay').join(' · '),'designer-overlay'));
      for(const edge of ['n','s','e','w','nw','ne','sw','se']){const handle=el('span',undefined,'designer-resize handle-'+edge);handle.dataset.edge=edge;handle.setAttribute('aria-hidden','true');node.append(handle);}
      node.onkeydown=e=>{
        if(e.key==='Enter'||e.key===' '){e.preventDefault();selectedTile=index;drawer='tile';render();root.querySelectorAll('.designer-tile')[index]?.focus();return;}
        const delta={ArrowLeft:[-1,0],ArrowRight:[1,0],ArrowUp:[0,-1],ArrowDown:[0,1]}[e.key];
        if(delta){e.preventDefault();selectedTile=index;drawer='tile';const [dx,dy]=delta;updateTile({...tile,...(e.shiftKey?{columnSpan:tile.columnSpan+dx,rowSpan:tile.rowSpan+dy}:{column:tile.column+dx,row:tile.row+dy})},index);root.querySelectorAll('.designer-tile')[index]?.focus();}
      };
      node.onpointerdown=e=>{
        if(e.button!==0)return;e.preventDefault();selectedTile=index;drawer='tile';
        board.querySelectorAll('.designer-tile').forEach(n=>n.classList.toggle('selected',n===node));
        const edge=e.target.dataset.edge||'',resize=!!edge,startX=e.clientX,startY=e.clientY,bounds=board.getBoundingClientRect();let candidate=copy(tile);
        node.setPointerCapture(e.pointerId);
        node.onpointermove=move=>{
          if(panImage&&!resize&&!["weather","aircraft"].includes(tile.kind)){
            const slackX=node.clientWidth-parseFloat(image.style.width),slackY=node.clientHeight-parseFloat(image.style.height);
            candidate={...tile,horizontalPositionPercent:Math.abs(slackX)>1?Math.round(Math.max(0,Math.min(100,(tile.horizontalPositionPercent??50)+(move.clientX-startX)/slackX*100))):(tile.horizontalPositionPercent??50),verticalPositionPercent:Math.abs(slackY)>1?Math.round(Math.max(0,Math.min(100,(tile.verticalPositionPercent??50)+(move.clientY-startY)/slackY*100))):(tile.verticalPositionPercent??50)};
            sizeImages(candidate,index);return;
          }
          const dx=proportions.cell(proportions.columns,(move.clientX-bounds.left)/bounds.width)-proportions.cell(proportions.columns,(startX-bounds.left)/bounds.width),dy=proportions.cell(proportions.rows,(move.clientY-bounds.top)/bounds.height)-proportions.cell(proportions.rows,(startY-bounds.top)/bounds.height);
          candidate={...tile,...(resize?{column:tile.column+(edge.includes('w')?dx:0),row:tile.row+(edge.includes('n')?dy:0),columnSpan:tile.columnSpan+(edge.includes('e')?dx:edge.includes('w')?-dx:0),rowSpan:tile.rowSpan+(edge.includes('s')?dy:edge.includes('n')?-dy:0)}:{column:tile.column+dx,row:tile.row+dy})};
          const swap=layout.tiles.some((other,i)=>i!==index&&other.row===candidate.row&&other.column===candidate.column&&other.rowSpan===tile.rowSpan&&other.columnSpan===tile.columnSpan&&candidate.rowSpan===tile.rowSpan&&candidate.columnSpan===tile.columnSpan);
          node.classList.toggle('invalid',!validTile(candidate,index)&&!swap);
          Object.assign(node.style,Object.fromEntries(Object.entries(proportions.bounds(candidate)).map(([k,v])=>[k,v*100+'%'])));
          sizeImages(candidate,index);
        };
        node.onpointerup=()=>{node.onpointermove=null;if(JSON.stringify(candidate)===JSON.stringify(tile))render();else updateTile(candidate,index);};
        node.onpointercancel=()=>render();
      };board.append(node);
    });
    const previewFoot=el('div',undefined,'designer-preview-foot');preview.append(previewFoot);
    const scaleLabel=el('span');previewFoot.append(el('span','Drag to move or swap · Edges resize · Arrows move · Shift + arrows resize'),scaleLabel);
    observer=new ResizeObserver(()=>{fitPreview();scaleLabel.textContent=outputWidth+' × '+outputHeight+' · '+Math.round(board.clientWidth/outputWidth*100)+'% preview';sizeImages();});observer.observe(board);observer.observe(root);requestAnimationFrame(fitPreview);
    const side=el('aside',undefined,'designer-inspector');side.hidden=!drawer;workspace.append(side);
    button('Close panel',()=>{drawer='';render();},side).classList.add('designer-close');
    const sizing=el('section',undefined,'designer-sizing');sizing.hidden=drawer!=='sizing';side.append(sizing);
    sizing.append(el('h3','Feed sizing'),el('p','Preview changes here, then Save or Apply. Choose Original, Fit, Fill or Stretch per tile. Small tiles remain equal in size.','designer-help'));
    button('Fit tiles to streams',()=>{
      if(layout.tiles.some(t=>['weather','aircraft'].includes(t.kind))){message('Use Fine sizing for a wall containing data tiles.');return;}
      const targets={};
      for(const tile of layout.tiles){const slot=tile.cameraSlot>0?tile.cameraSlot:previewCameras.get(layout.id+':'+tile.cameraSlot);const ratio=streamAspects.get(slot);
        if(!ratio){message('Load each stream preview and choose preview streams for focus tiles before fitting.');return;}targets[tile.cameraSlot]=ratio;}
      const fit=wallProportions.fit(layout,targets);
      if(JSON.stringify(fit.rows)===JSON.stringify(proportions.rows)&&JSON.stringify(fit.columns)===JSON.stringify(proportions.columns)){message('This arrangement is already the best fit found while keeping small tiles equal. No changes made.');return;}
      layout.rowWeights=fit.rows;layout.columnWeights=fit.columns;changed();message('Fit preview ready. Compare the wall, Undo if needed, then Save or Apply.');
    },sizing);
    button('Reset sizing',()=>{layout.rowWeights=[];layout.columnWeights=[];changed();},sizing);
    sizing.append(el('p','Fine sizing adjusts shared grid tracks in 0.5% steps. Linked tracks move together to keep small tiles equal. Positions and stream assignments stay in their grid cells.','designer-help'));
    for(const axis of ['rows','columns']){
      const details=el('details');details.append(el('summary',axis==='rows'?'Row heights':'Column widths'));sizing.append(details);
      proportions[axis].forEach((value,index)=>{
        const input=el('input');input.type='number';input.min='2';input.max='98';input.step='.5';input.value=(value*100).toFixed(2);
        input.onchange=()=>{const next=wallProportions.adjust(layout,axis,index,Number(input.value)/100);if(!next){render();message('These tracks are linked or the requested size leaves too little space.');return;}Object.assign(layout,next);changed();};
        field((axis==='rows'?'Row ':'Column ')+(index+1)+' (%)',input,details);
      });
    }
    const tilePanel=el('div',undefined,'designer-tile-panel');tilePanel.hidden=drawer!=='tile';side.append(tilePanel);
    const tile=layout.tiles[selectedTile];if(tile&&['weather','aircraft'].includes(tile.kind)&&!pluginsUi.enabled(tile.kind))return;
    if(tile?.kind==='weather'){
      tilePanel.append(el('h3',tile.weather.location||'Weather'),el('p','Drag or resize this tile just like a stream. Changes stay in the layout draft.','designer-help'));
      button('Edit weather',()=>weatherUi.editor(tile.weather,weather=>updateTile({...tile,weather},selectedTile)),tilePanel);
      for(const [key,label,offset] of [['row','Row',1],['column','Column',1],['rowSpan','Height',0],['columnSpan','Width',0]]){const n=el('input');n.type='number';n.min=1;n.max=key==='row'||key==='rowSpan'?layout.rows:layout.columns;n.value=tile[key]+offset;n.onchange=()=>updateTile({...tile,[key]:Number(n.value)-offset},selectedTile);field(label,n,tilePanel);}
      button('Remove from layout',()=>{layout.tiles.splice(selectedTile,1);selectedTile=-1;changed();},tilePanel);
    }
    if(tile?.kind==='aircraft'){
      tilePanel.append(el('h3',tile.aircraft.location||'Aircraft'),el('p','Drag or resize this tile just like a stream. Changes stay in the layout draft.','designer-help'));
      const fallback=el('select');fallback.add(new Option('Keep permanent aircraft tile',''));for(const c of config.cameras.filter(c=>!layout.tiles.some(t=>t.cameraSlot===c.slot)))fallback.add(new Option(c.name,c.slot));field('Camera behind aircraft',fallback,tilePanel);button('Appear over selected camera',()=>{if(fallback.value)updateTile({...tile,kind:'camera',cameraSlot:Number(fallback.value)},selectedTile);},tilePanel);
      button('Edit aircraft',()=>aircraftUi.editor(tile.aircraft,aircraft=>updateTile({...tile,aircraft},selectedTile)),tilePanel);
      for(const [key,label,offset] of [['row','Row',1],['column','Column',1],['rowSpan','Height',0],['columnSpan','Width',0]]){const n=el('input');n.type='number';n.min=1;n.max=key==='row'||key==='rowSpan'?layout.rows:layout.columns;n.value=tile[key]+offset;n.onchange=()=>updateTile({...tile,[key]:Number(n.value)-offset},selectedTile);field(label,n,tilePanel);}
      button('Remove from layout',()=>{layout.tiles.splice(selectedTile,1);selectedTile=-1;changed();},tilePanel);
    }
    if(tile&&!['weather','aircraft'].includes(tile.kind)){
      const camera=config.cameras.find(camera=>camera.slot===tile.cameraSlot)||{name:tile.cameraSlot<0?'Focus '+(-tile.cameraSlot):'Unavailable camera '+tile.cameraSlot};
      const selected=el('div',undefined,'designer-selected');selected.append(el('span','SELECTED TILE','designer-eyebrow'),el('h3',camera.name));tilePanel.append(selected);
      const cameraSelect=el('select');for(const camera of config.cameras)cameraSelect.add(new Option(camera.name+' · #'+camera.slot,camera.slot));cameraSelect.value=tile.cameraSlot;
      cameraSelect.onchange=()=>updateTile({...tile,cameraSlot:Number(cameraSelect.value)},selectedTile);if(tile.cameraSlot>0)field('Stream',cameraSelect,tilePanel);else {
        const pick=el('select');pick.add(new Option('No preview camera','0'));for(const camera of config.cameras)pick.add(new Option(camera.name,camera.slot));
        pick.value=previewCameras.get(layout.id+':'+tile.cameraSlot)||0;pick.onchange=()=>{previewCameras.set(layout.id+':'+tile.cameraSlot,Number(pick.value));render();};field('Preview camera only',pick,tilePanel);
        tilePanel.append(el('p','Your automation rule chooses the live camera.','designer-help'));
      }
      if(isAutomation&&tile.cameraSlot>0){const focusActions=el('div',undefined,'designer-focus-actions');tilePanel.append(focusActions);for(const focusSlot of layout.focusSlots)button('Use as Focus '+(-focusSlot),()=>{const old=layout.tiles.find(t=>t.cameraSlot===focusSlot);old.cameraSlot=tile.cameraSlot;tile.cameraSlot=focusSlot;changed();},focusActions);}
      if(!isAutomation){
        if(pluginsUi.enabled('aircraft')){const aircraftSection=el('section',undefined,'designer-aircraft-controls');aircraftSection.append(el('h4','Aircraft display'));tilePanel.append(aircraftSection);
        aircraftSection.append(el('p','Widget stays visible in a corner. Appear over camera fills this tile while aircraft are nearby. Permanent tile always shows aircraft information.','designer-help'));
        if(tile.cameraSlot>0&&tile.cameraSlot<=32&&!(tile.cameraSlot>=10&&tile.cameraSlot<=25))button('Aircraft widget',()=>aircraftUi.overlayEditor(tile.cameraSlot),aircraftSection);
        if(tile.aircraft){aircraftSection.append(aircraftUi.status(tile.aircraft));button('Edit appear-over-camera display',()=>aircraftUi.editor(tile.aircraft,aircraft=>updateTile({...tile,aircraft},selectedTile)),aircraftSection);button('Remove appear-over-camera display',()=>updateTile({...tile,aircraft:null},selectedTile),aircraftSection);}
        else button('Appear over camera when nearby',()=>aircraftUi.editor(aircraftUi.defaults(),aircraft=>updateTile({...tile,aircraft},selectedTile)),aircraftSection);
        button('Permanent aircraft tile',()=>aircraftUi.editor(tile.aircraft||aircraftUi.defaults(),aircraft=>updateTile({...tile,kind:'aircraft',itemId:layoutItemId(),cameraSlot:0,weather:null,aircraft},selectedTile)),aircraftSection);
        if(tile.aircraft){const opacity=el('input');opacity.type='number';opacity.min=0;opacity.max=100;opacity.step=1;opacity.value=tile.aircraft.backgroundOpacity;opacity.onchange=()=>{if(opacity.reportValidity())updateTile({...tile,aircraft:{...tile.aircraft,backgroundOpacity:Number(opacity.value)}},selectedTile);};field('Aircraft background opacity (%)',opacity,aircraftSection);aircraftSection.append(el('p','0% shows the camera behind the text; 100% hides the camera.','designer-help'));}
        } if(pluginsUi.enabled('weather')){const content=el('details');content.append(el('summary','Widgets and tile content'));tilePanel.append(content);const actions=el('div',undefined,'designer-content-actions');content.append(actions);
        if(tile.cameraSlot>0&&tile.cameraSlot<=32&&!(tile.cameraSlot>=10&&tile.cameraSlot<=25)){button('Weather Widget',()=>weatherUi.overlayEditor(tile.cameraSlot),actions);if(pluginsUi.enabled('aircraft'))button('Aircraft Widget',()=>aircraftUi.overlayEditor(tile.cameraSlot),actions);}
        button('Replace with weather',()=>weatherUi.editor(weatherUi.defaults(),weather=>updateTile({...tile,kind:'weather',itemId:layoutItemId(),cameraSlot:0,aircraft:null,weather},selectedTile)),actions);
      }
      } const sizing=el('select');for(const [value,label] of [['fit','Fit · entire image (default)'],['original','Original size'],['fill','Fill · crop edges'],['stretch','Stretch to tile']])sizing.add(new Option(label,value));sizing.value=tile.sizing||'fit';sizing.onchange=()=>updateTile({...tile,sizing:sizing.value},selectedTile);field('Image sizing · this tile',sizing,tilePanel);
      const advanced=el('details');advanced.append(el('summary','Advanced image transform'));tilePanel.append(advanced);const framing=el('div',undefined,'designer-framing');advanced.append(framing);
      for(const [key,label,min,max,fallback] of [['zoomPercent','Zoom',25,400,100],['horizontalPositionPercent','Horizontal position',0,100,50],['verticalPositionPercent','Vertical position',0,100,50]]){
        const input=el('input');input.type='range';input.min=min;input.max=max;input.step=1;input.value=tile[key]??fallback;
        const wrapper=el('label'),caption=el('span',label+' · '+input.value+'%');input.setAttribute('aria-label',label);const number=el('input');number.type='number';number.min=min;number.max=max;number.value=input.value;number.setAttribute('aria-label',label+' value');wrapper.append(caption,input,number);framing.append(wrapper);
        input.oninput=()=>{tile[key]=Number(input.value);number.value=input.value;caption.textContent=label+' · '+input.value+'%';sizeImages();};
        input.onchange=()=>{changed();};number.onchange=()=>{if(!number.reportValidity())return;updateTile({...tile,[key]:Number(number.value)},selectedTile);};
      }
      button('Reset image framing',()=>updateTile({...tile,zoomPercent:100,horizontalPositionPercent:50,verticalPositionPercent:50},selectedTile),framing);
      const exact=el('details');exact.append(el('summary','Exact position and size'));tilePanel.append(exact);const properties=el('div',undefined,'designer-properties');exact.append(properties);
      for(const [key,label,offset] of [['row','Row',1],['column','Column',1],['rowSpan','Height',0],['columnSpan','Width',0]]){
        const input=el('input');input.type='number';input.min=1;input.max=key==='row'||key==='rowSpan'?layout.rows:layout.columns;input.value=tile[key]+offset;
        input.onchange=()=>updateTile({...tile,[key]:Number(input.value)-offset},selectedTile);field(label,input,properties);
      }

      tilePanel.append(el('p',Math.round(proportions.bounds(tile).width*outputWidth)+' × '+Math.round(proportions.bounds(tile).height*outputHeight)+' output pixels. Original uses source pixels, centered and clipped. Fit keeps the whole image; Fill crops; Stretch changes proportions.','designer-help'));
      if((tile.sizing||'fit')==='original'){const note=el('p','Source dimensions unavailable. Preview uses Fit until live camera dimensions arrive.','designer-help');note.dataset.dimensionsNote=tile.cameraSlot>0?tile.cameraSlot:(previewCameras.get(layout.id+":"+tile.cameraSlot)||0);note.hidden=streamDimensions.has(Number(note.dataset.dimensionsNote));tilePanel.append(note);}
      button('Fill available space',()=>{let best=tile;for(let r=1;r<=layout.rows-tile.row;r++)for(let c=1;c<=layout.columns-tile.column;c++){const t={...tile,rowSpan:r,columnSpan:c};if(r*c>best.rowSpan*best.columnSpan&&validTile(t,selectedTile))best=t;}updateTile(best,selectedTile);},tilePanel);
      const overlays=[config.doorbellOverlay,config.garageOverlay,...(config.additionalOverlays||[])].filter(o=>o.camera.enabled&&o.hostCameraSlot===tile.cameraSlot);
      if(overlays.length){const hosts=el('div',undefined,'designer-hosts');hosts.append(el('span','OVERLAYS','designer-eyebrow'));for(const o of overlays)hosts.append(el('span',o.camera.name,'designer-host'));tilePanel.append(hosts);}
      const remove=button('Remove from layout',()=>{layout.tiles.splice(selectedTile,1);selectedTile=-1;changed();},tilePanel);remove.classList.add('designer-danger');remove.disabled=(isAutomation&&layout.focusSlots.includes(tile.cameraSlot));
    }
    const available=config.cameras.filter(camera=>!layout.tiles.some(t=>t.cameraSlot===camera.slot));
    const library=el('section',undefined,'designer-library');library.hidden=drawer!=='add';side.append(library);
    const search=el('input');search.type='search';search.placeholder='Search cameras';search.setAttribute('aria-label','Search cameras');search.value=searchText;library.append(search);
    search.oninput=()=>{searchText=search.value;for(const item of library.querySelectorAll('.designer-camera'))item.hidden=!item.dataset.name.includes(searchText.toLowerCase());};
    const libraryHead=el('div');libraryHead.append(el('h3','Available streams'),el('span',String(available.length),'designer-count'));library.append(libraryHead);
    if(!available.length)library.append(el('p','All streams are on this layout. Add more from Streams.','designer-empty'));
    else library.append(el('p','Choose a camera, then click or drag across empty cells. Or drag a camera onto the wall.','designer-help'));
    for(const camera of available){
      const node=button('',()=>{
        armedCamera=armedCamera===camera.slot?null:camera.slot;render();message(armedCamera===null?'Placement canceled.':'Click or drag across empty cells for '+camera.name+'.');
      },library);node.classList.add('designer-camera');node.classList.toggle('armed',armedCamera===camera.slot);node.setAttribute('aria-pressed',String(armedCamera===camera.slot));node.dataset.name=camera.name.toLowerCase();node.hidden=!node.dataset.name.includes(searchText.toLowerCase());node.setAttribute('aria-label','Add '+camera.name+' to layout');
      const thumb=el('img');thumb.alt='';dashboardUX.snapshot(thumb,camera.slot);thumb.draggable=false;thumb.onerror=()=>thumb.style.visibility='hidden';
      node.append(thumb,el('span',camera.name),el('span','+'));node.draggable=true;node.ondragstart=e=>e.dataTransfer.setData('text/plain',String(camera.slot));
    }
    const mode=dragMode.closest('label');previewHead.append(mode);presets.hidden=drawer!=='presets';side.append(presets);
    gridSettings.hidden=drawer!=='advanced';side.append(gridSettings);
    if(isAutomation){
      const count=el('select');count.add(new Option('One focus tile','1'));count.add(new Option('Two focus tiles','2'));count.value=layout.focusSlots.length;
      count.onchange=()=>{if(Number(count.value)===1){layout.tiles=layout.tiles.filter(t=>t.cameraSlot!==-2);layout.focusSlots=[-1];}else{const target=layout.tiles.find(t=>t.cameraSlot>0);if(!target){message('Add a camera tile first, then select two focus tiles.');count.value=layout.focusSlots.length;return;}target.cameraSlot=-2;layout.focusSlots=[-1,-2];}selectedTile=-1;changed();};field('Focus tiles',count,canvasControls);
    }
    const help=el('section');help.hidden=drawer!=='help';help.append(el('h3','Arrange your wall'),el('p','Drag tiles to move or swap them. Drag the right or bottom edge to resize. Move smaller tiles into empty spaces before enlarging a focus tile.'),el('p','Select a tile to change its camera or enter exact dimensions. Changes stay in draft until saved or applied.'));
    if(isAutomation){help.append(el('p','Focus tiles get their cameras from automation rules. Preview cameras only help you try the arrangement.'));button('Open Automation rules',()=>adminLayout.select('automation'),help);}
    side.append(help);
    status=el('p',dirty?'Unsaved changes. Apply when you are ready.':'Apply to wall saves all layout drafts and displays the selected layout. Save layout saves drafts without switching the wall.','designer-status');status.setAttribute('role','status');status.tabIndex=-1;root.append(status);
    const hiddenOverlays=[config.doorbellOverlay,config.garageOverlay,...(config.additionalOverlays||[])]
      .filter(overlay=>overlay.camera.enabled&&!layout.tiles.some(tile=>tile.cameraSlot===overlay.hostCameraSlot));
    if(pluginsUi.enabled('pictureInPicture')&&hiddenOverlays.length)root.append(el('p',hiddenOverlays.map(o=>o.camera.name).join(', ')+' hidden in this layout because the host stream is absent.','designer-help'));
  }
  return {open(id){if(draft?.layouts.some(l=>l.id===id)){selectedId=id;selectedTile=-1;render();root.querySelector('select')?.focus();}},dimensions(cameras){streamDimensions=new Map((cameras||[]).filter(c=>c.width>0&&c.height>0).map(c=>[c.slot,c]));if(draft)sizeImages();},isDirty:()=>dirty,savedLayouts:()=>saved?.layouts||[],active:()=>saved?.layouts.find(l=>l.id===saved.activeLayoutId),updateCameras(cameras){if(config){config.cameras=cameras;render();}},load(value){
    config=value;root=document.querySelector(isAutomation?'#automation-layout-editor':'#standard-layout-editor');
    if(!root)return;
    saved=copy(isAutomation?{layouts:value.automationViewLayouts,activeLayoutId:value.automationViewLayouts[0].id,revision:value.automationLayoutRevision}:{layouts:value.layouts,activeLayoutId:value.activeLayoutId,revision:value.layoutRevision});
    if(!dirty){draft=copy(saved);selectedId=saved.activeLayoutId;selectedTile=-1;undoHistory.length=0;redoHistory.length=0;}
    render();
  }};
}
const standardWallDesigner=createWallDesigner(), automationWallDesigner=createWallDesigner(true);
const wallDesigner=(()=>{
  let tab='standard';
  function selectTab(value){tab=value;for(const name of ['standard','automation']){document.querySelector('#'+name+'-layout-editor').hidden=name!==tab;document.querySelector('[data-layout-tab="'+name+'"]').setAttribute('aria-selected',String(name===tab));document.querySelector('[data-layout-tab="'+name+'"]').tabIndex=name===tab?0:-1;}}
  return {
    dimensions(cameras){standardWallDesigner.dimensions(cameras);automationWallDesigner.dimensions(cameras);},
    savedLayouts:()=>standardWallDesigner.savedLayouts(),isDirty:()=>standardWallDesigner.isDirty()||automationWallDesigner.isDirty(),active:()=>standardWallDesigner.active(),
    updateCameras(cameras){standardWallDesigner.updateCameras(cameras);automationWallDesigner.updateCameras(cameras);},
    openLayout(id,automation=false){adminLayout.select('layouts');selectTab(automation?'automation':'standard');(automation?automationWallDesigner:standardWallDesigner).open(id);},
    openAutomation(){adminLayout.select('layouts');selectTab('automation');},
    load(value){const page=document.querySelector('#page-layouts');if(!document.querySelector('#standard-layout-editor')){const nav=document.createElement('nav');nav.className='system-tabs';nav.setAttribute('role','tablist');nav.setAttribute('aria-label','Layout type');for(const [id,label] of [['standard','Standard View layouts'],['automation','Automation layouts']]){const b=document.createElement('button');b.type='button';b.textContent=label;b.dataset.layoutTab=id;b.id='layout-tab-'+id;b.setAttribute('role','tab');b.setAttribute('aria-controls',id+'-layout-editor');b.onkeydown=e=>{if(['ArrowLeft','ArrowRight','Home','End'].includes(e.key)){e.preventDefault();const next=e.key==='Home'?'standard':e.key==='End'?'automation':id==='standard'?'automation':'standard';selectTab(next);document.querySelector('[data-layout-tab="'+next+'"]').focus();}};b.onclick=()=>selectTab(id);nav.append(b);}page.append(nav);for(const id of ['standard','automation']){const panel=document.createElement('section');panel.id=id+'-layout-editor';panel.setAttribute('role','tabpanel');panel.setAttribute('aria-labelledby','layout-tab-'+id);panel.tabIndex=0;page.append(panel);}}
      standardWallDesigner.load(value);automationWallDesigner.load(value);selectTab(tab);
    }
  };
})();
