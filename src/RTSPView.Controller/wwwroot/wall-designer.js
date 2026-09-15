function createWallDesigner(isAutomation = false) {
  let config, saved, draft, selectedId, selectedTile = 0, previous, dirty = false, busy = false;
  let root, board, status;
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
    const wrapper = el('label', label); wrapper.append(input); parent.append(wrapper); return input;
  }
  function message(text) { status.textContent = text; }
  function changed() { dirty = true; render(); message(isAutomation ? 'Unsaved automation layout. Save when ready.' : 'Unsaved draft. Apply when ready to change the wall.'); }
  function validTile(candidate, index) {
    const layout = current();
    return [candidate.row,candidate.column,candidate.rowSpan,candidate.columnSpan].every(Number.isInteger) &&
      candidate.row >= 0 && candidate.column >= 0 && candidate.rowSpan >= 1 && candidate.columnSpan >= 1 &&
      candidate.row + candidate.rowSpan <= layout.rows && candidate.column + candidate.columnSpan <= layout.columns &&
      !layout.tiles.some((tile,i) => i !== index && (tile.cameraSlot === candidate.cameraSlot ||
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
    Object.assign(layout,wallLayoutPresets.create(id,layout.aspectRatio||'16:9',config.cameras.map(camera=>camera.slot)));
    if(isAutomation) layout.focusSlots=layout.tiles.slice(0,id==='dual'?2:1).map(t=>t.cameraSlot);
    selectedTile=0;changed();
  }
  function addCamera(slot, row, column) {
    const tile = {cameraSlot:slot,row,column,rowSpan:1,columnSpan:1};
    if (!validTile(tile,-1)) { message('Choose an empty cell and a stream not already in this layout.'); return; }
    current().tiles.push(tile); selectedTile = current().tiles.length-1; changed();
  }
  async function persist(apply = false, revert = false) {
    if (busy) return;
    const payload = revert ? copy(previous) : copy(draft);
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
      if (!draft.layouts.some(layout=>layout.id===selectedId)) selectedId = draft.activeLayoutId;
      render(); dashboardUX.sync(); message(revert ? 'Previous saved layouts restored.' : apply ? 'Layout applied. The viewer will update shortly.' : 'Layouts saved.');
      if(isAutomation){message('Automation layouts saved. Choose one in a focused-layout rule.');await automationUi.refreshLayouts();}
    } catch(error) { message(error.message); }
    finally { busy = false; root.inert = false; }
  }
  function render() {
    root.replaceChildren();
    const layout=current();
    const top=el('div',undefined,'designer-toolbar');root.append(top);
    const picker=el('div',undefined,'designer-layout-picker');top.append(picker);
    const layouts=el('select');
    for(const item of draft.layouts)layouts.add(new Option(item.name,item.id));
    layouts.value=selectedId;layouts.onchange=()=>{selectedId=layouts.value;selectedTile=0;render();};
    field('Saved layout',layouts,picker);
    const layoutTools=el('div',undefined,'designer-layout-tools');picker.append(layoutTools);
    const badge=el('span',dirty?'Unsaved changes':!isAutomation&&selectedId===saved.activeLayoutId?'On wall':'Saved','designer-badge'+(dirty?' draft':''));layoutTools.append(badge);
    const manage=el('details',undefined,'designer-menu');manage.append(el('summary','Manage layout'));layoutTools.append(manage);
    const menu=el('div',undefined,'designer-popover');manage.append(menu);
    const name=el('input');name.value=layout.name;name.maxLength=80;
    name.oninput=()=>{layout.name=name.value.trim();dirty=true;badge.textContent='Unsaved changes';badge.classList.add('draft');root.querySelector('.designer-discard').disabled=false;layouts.selectedOptions[0].textContent=layout.name;message('Unsaved changes.');};field('Layout name',name,menu);
    button('Duplicate layout',()=>{
      if(draft.layouts.length>=32){message('You can save up to 32 layouts.');return;}
      const item=copy(layout);item.id='layout-'+Date.now().toString(36)+'-'+Math.random().toString(36).slice(2,8);
      item.name=(item.name+' copy').slice(0,80);draft.layouts.push(item);selectedId=item.id;changed();
    },menu);
    const deleteButton=button('Delete layout',()=>{
      draft.layouts=draft.layouts.filter(item=>item.id!==selectedId);selectedId=isAutomation?draft.layouts[0].id:draft.activeLayoutId;selectedTile=0;changed();
    },menu);deleteButton.disabled=isAutomation?draft.layouts.length===1:selectedId===saved.activeLayoutId;deleteButton.classList.add('designer-danger');
    const revert=button('Revert last save',()=>persist(false,true),menu);revert.disabled=!previous;
    const actions=el('div',undefined,'designer-actions');top.append(actions);
    button('Discard changes',()=>{draft=copy(saved);selectedId=saved.activeLayoutId;selectedTile=0;dirty=false;render();message('Draft discarded.');},actions).classList.add('designer-discard');root.querySelector('.designer-discard').disabled=!dirty;
    if(isAutomation)button('Save automation layouts',()=>persist(),actions,false);
    else {if(selectedId!==saved.activeLayoutId)button('Save layout',()=>persist(),actions);button('Apply changes',()=>persist(true),actions,false);}
    if(isAutomation){
      const help=el('p','Focus positions follow active detections in order. With only one active camera, the second position keeps its assigned fallback camera. Other tiles retain their cameras; a focused camera already on the layout swaps places to avoid duplicates.','designer-help');root.append(help);
      const focusControls=el('div',undefined,'designer-canvas-controls');root.append(focusControls);
      const count=el('select');count.add(new Option('One focus position','1'));count.add(new Option('Two focus positions','2'));count.value=layout.focusSlots.length;
      count.onchange=()=>{if(Number(count.value)>layout.tiles.length){message('Add a second tile first.');return;}layout.focusSlots=layout.tiles.slice(0,Number(count.value)).map(t=>t.cameraSlot);changed();};field('Focus positions',count,focusControls);
      layout.focusSlots.forEach((slot,i)=>{const pick=el('select');for(const t of layout.tiles)pick.add(new Option(config.cameras.find(c=>c.slot===t.cameraSlot)?.name||'Camera '+t.cameraSlot,t.cameraSlot));pick.value=slot;pick.onchange=()=>{if(layout.focusSlots.some((s,j)=>j!==i&&s===Number(pick.value))){message('Choose different focus positions.');pick.value=slot;return;}layout.focusSlots[i]=Number(pick.value);changed();};field('Focus '+(i+1)+' position / fallback',pick,focusControls);});
      button('Open Automation rules',()=>adminLayout.select('automation'),focusControls);
    }
    const workspace=el('div',undefined,'designer-workspace');root.append(workspace);
    const preview=el('section',undefined,'designer-preview');workspace.append(preview);
    const previewHead=el('div',undefined,'designer-preview-head');preview.append(previewHead);
    const title=el('div');title.append(el('h2','Wall preview'),el('span',layout.rows+' × '+layout.columns+' grid · '+layout.tiles.length+' streams','designer-meta'));previewHead.append(title);
    const canvasControls=el('div',undefined,'designer-canvas-controls');preview.append(canvasControls);
    const format=el('select');format.add(new Option('Landscape · 16:9','16:9'));format.add(new Option('Portrait · 9:16','9:16'));format.value=layout.aspectRatio||'16:9';
    format.onchange=()=>{Object.assign(layout,wallLayoutPresets.transpose(layout));layout.aspectRatio=format.value;changed();};field('Screen format',format,canvasControls);
    const gridFields=el('div',undefined,'designer-grid-fields');canvasControls.append(gridFields);
    for(const [key,label] of [['rows','Rows'],['columns','Columns']]){
      const input=el('input');input.type='number';input.min=1;input.max=isAutomation?6:4;input.value=layout[key];
      input.onchange=()=>{
        const value=Number(input.value),old=layout[key];layout[key]=value;
        if(!Number.isInteger(value)||value<1||value>(isAutomation?6:4)||layout.tiles.some((t,i)=>!validTile(t,i))){layout[key]=old;render();message('Remove or resize tiles before shrinking the grid.');return;}
        changed();
      };field(label,input,gridFields);
    }
    const presets=el('details',undefined,'designer-preset-library');preview.append(presets);
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
    board.style.setProperty('--aspect',(layout.aspectRatio||'16:9').replace(':','/'));board.style.setProperty('--ratio',layout.aspectRatio==='9:16'?9/16:16/9);board.style.setProperty('--rows',layout.rows);board.style.setProperty('--columns',layout.columns);stage.append(board);
    board.ondragover=e=>e.preventDefault();board.ondrop=e=>{e.preventDefault();const slot=Number(e.dataTransfer.getData('text/plain'));if(!config.cameras.some(c=>c.slot===slot))return;const bounds=board.getBoundingClientRect();addCamera(slot,Math.floor((e.clientY-bounds.top)/bounds.height*layout.rows),Math.floor((e.clientX-bounds.left)/bounds.width*layout.columns));};
    layout.tiles.forEach((tile,index)=>{
      const camera=config.cameras.find(camera=>camera.slot===tile.cameraSlot)||{name:'Stream',slot:tile.cameraSlot};
      const node=el('div',undefined,'designer-tile'+(index===selectedTile?' selected':''));node.tabIndex=0;
      node.setAttribute('aria-label',camera.name+'; row '+(tile.row+1)+', column '+(tile.column+1));
      Object.assign(node.style,{left:tile.column/layout.columns*100+'%',top:tile.row/layout.rows*100+'%',width:tile.columnSpan/layout.columns*100+'%',height:tile.rowSpan/layout.rows*100+'%'});
      const image=el('img');image.alt='';image.draggable=false;dashboardUX.snapshot(image,tile.cameraSlot);image.onerror=()=>image.style.visibility='hidden';node.append(image);
      node.append(el('span',camera.name,'designer-caption'));
      if(isAutomation&&layout.focusSlots.includes(tile.cameraSlot))node.append(el('span','Focus '+(layout.focusSlots.indexOf(tile.cameraSlot)+1),'designer-overlay'));
      const overlays=[config.doorbellOverlay,config.garageOverlay,...(config.additionalOverlays||[])].map(overlay=>[overlay.camera.name,overlay]).filter(([,o])=>o.camera.enabled&&o.hostCameraSlot===tile.cameraSlot);
      if(overlays.length)node.append(el('span',overlays.map(([name])=>name+' overlay').join(' · '),'designer-overlay'));
      const handle=el('span','↘','designer-resize');handle.setAttribute('aria-hidden','true');node.append(handle);
      node.onkeydown=e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();selectedTile=index;render();}};
      node.onpointerdown=e=>{
        if(e.button!==0)return;e.preventDefault();selectedTile=index;
        const resize=e.target===handle,startX=e.clientX,startY=e.clientY,bounds=board.getBoundingClientRect();let candidate=copy(tile);
        node.setPointerCapture(e.pointerId);
        node.onpointermove=move=>{
          const dx=Math.round((move.clientX-startX)/bounds.width*layout.columns),dy=Math.round((move.clientY-startY)/bounds.height*layout.rows);
          candidate={...tile,...(resize?{columnSpan:tile.columnSpan+dx,rowSpan:tile.rowSpan+dy}:{column:tile.column+dx,row:tile.row+dy})};
          const swap=layout.tiles.some((other,i)=>i!==index&&other.row===candidate.row&&other.column===candidate.column&&other.rowSpan===tile.rowSpan&&other.columnSpan===tile.columnSpan&&candidate.rowSpan===tile.rowSpan&&candidate.columnSpan===tile.columnSpan);
          node.classList.toggle('invalid',!validTile(candidate,index)&&!swap);
          Object.assign(node.style,{left:candidate.column/layout.columns*100+'%',top:candidate.row/layout.rows*100+'%',width:Math.max(1,candidate.columnSpan)/layout.columns*100+'%',height:Math.max(1,candidate.rowSpan)/layout.rows*100+'%'});
        };
        node.onpointerup=()=>{node.onpointermove=null;if(JSON.stringify(candidate)===JSON.stringify(tile))render();else updateTile(candidate,index);};
        node.onpointercancel=()=>render();
      };board.append(node);
    });
    const previewFoot=el('div',undefined,'designer-preview-foot');preview.append(previewFoot);
    previewFoot.append(el('span','Drag to move or swap. Use the corner handle to resize.'),el('span','Snapshot preview · '+(layout.aspectRatio||'16:9')));
    const side=el('aside',undefined,'designer-inspector');workspace.append(side);
    const tile=layout.tiles[selectedTile];
    if(tile){
      const camera=config.cameras.find(camera=>camera.slot===tile.cameraSlot)||{name:'Unavailable camera '+tile.cameraSlot};
      const selected=el('div',undefined,'designer-selected');selected.append(el('span','SELECTED TILE','designer-eyebrow'),el('h3',camera.name));side.append(selected);
      const cameraSelect=el('select');for(const camera of config.cameras)cameraSelect.add(new Option(camera.name+' · #'+camera.slot,camera.slot));cameraSelect.value=tile.cameraSlot;
      cameraSelect.onchange=()=>updateTile({...tile,cameraSlot:Number(cameraSelect.value)},selectedTile);field('Stream',cameraSelect,side);
      const properties=el('div',undefined,'designer-properties');side.append(properties);
      for(const [key,label,offset] of [['row','Row',1],['column','Column',1],['rowSpan','Height',0],['columnSpan','Width',0]]){
        const input=el('input');input.type='number';input.min=1;input.max=isAutomation?6:4;input.value=tile[key]+offset;
        input.onchange=()=>updateTile({...tile,[key]:Number(input.value)-offset},selectedTile);field(label,input,properties);
      }
      side.append(el('p','Position and size in grid cells.','designer-help'));
      const overlays=[config.doorbellOverlay,config.garageOverlay,...(config.additionalOverlays||[])].filter(o=>o.camera.enabled&&o.hostCameraSlot===tile.cameraSlot);
      if(overlays.length){const hosts=el('div',undefined,'designer-hosts');hosts.append(el('span','OVERLAYS','designer-eyebrow'));for(const o of overlays)hosts.append(el('span',o.camera.name,'designer-host'));side.append(hosts);}
      const remove=button('Remove from layout',()=>{layout.tiles.splice(selectedTile,1);selectedTile=0;changed();},side);remove.classList.add('designer-danger');remove.disabled=layout.tiles.length===1||(isAutomation&&layout.focusSlots.includes(tile.cameraSlot));
    }
    const available=config.cameras.filter(camera=>!layout.tiles.some(t=>t.cameraSlot===camera.slot));
    const library=el('section',undefined,'designer-library');side.append(library);
    const libraryHead=el('div');libraryHead.append(el('h3','Available streams'),el('span',String(available.length),'designer-count'));library.append(libraryHead);
    if(!available.length)library.append(el('p','All streams are on this layout. Add more from Streams.','designer-empty'));
    else library.append(el('p','Drag onto the wall or select to add.','designer-help'));
    for(const camera of available){
      const node=button('',()=>{
        for(let row=0;row<layout.rows;row++)for(let column=0;column<layout.columns;column++)if(validTile({cameraSlot:camera.slot,row,column,rowSpan:1,columnSpan:1},-1)){addCamera(camera.slot,row,column);return;}
        message('No empty cell is available. Remove a tile or enlarge the grid.');
      },library);node.classList.add('designer-camera');node.setAttribute('aria-label','Add '+camera.name+' to layout');
      const thumb=el('img');thumb.alt='';dashboardUX.snapshot(thumb,camera.slot);thumb.draggable=false;thumb.onerror=()=>thumb.style.visibility='hidden';
      node.append(thumb,el('span',camera.name),el('span','+'));node.draggable=true;node.ondragstart=e=>e.dataTransfer.setData('text/plain',String(camera.slot));
    }
    status=el('p',dirty?'Unsaved changes. Apply when you are ready.':'Changes stay in draft until you apply.','designer-status');status.setAttribute('role','status');root.append(status);
    const hiddenOverlays=[config.doorbellOverlay,config.garageOverlay,...(config.additionalOverlays||[])]
      .filter(overlay=>overlay.camera.enabled&&!layout.tiles.some(tile=>tile.cameraSlot===overlay.hostCameraSlot));
    if(hiddenOverlays.length)root.append(el('p',hiddenOverlays.map(o=>o.camera.name).join(', ')+' hidden in this layout because the host stream is absent.','designer-help'));
  }
  window.addEventListener('beforeunload',event=>{if(dirty){event.preventDefault();event.returnValue='';}});
  return {isDirty:()=>dirty,active:()=>saved?.layouts.find(l=>l.id===saved.activeLayoutId),updateCameras(cameras){if(config){config.cameras=cameras;render();}},load(value){
    config=value;root=document.querySelector(isAutomation?'#automation-layout-editor':'#standard-layout-editor');
    if(!root)return;
    saved=copy(isAutomation?{layouts:value.automationViewLayouts,activeLayoutId:value.automationViewLayouts[0].id}:{layouts:value.layouts,activeLayoutId:value.activeLayoutId});
    if(!dirty){draft=copy(saved);selectedId=saved.activeLayoutId;selectedTile=0;}
    render();
  }};
}
const standardWallDesigner=createWallDesigner(), automationWallDesigner=createWallDesigner(true);
const wallDesigner=(()=>{
  let tab='standard';
  function selectTab(value){tab=value;adminUi.collapseSections();for(const name of ['standard','automation']){document.querySelector('#'+name+'-layout-editor').hidden=name!==tab;document.querySelector('[data-layout-tab="'+name+'"]').setAttribute('aria-selected',String(name===tab));}}
  return {
    isDirty:()=>standardWallDesigner.isDirty()||automationWallDesigner.isDirty(),active:()=>standardWallDesigner.active(),
    updateCameras(cameras){standardWallDesigner.updateCameras(cameras);automationWallDesigner.updateCameras(cameras);},
    openAutomation(){adminLayout.select('layouts');selectTab('automation');},
    load(value){const page=document.querySelector('#page-layouts');if(!document.querySelector('#standard-layout-editor')){const nav=document.createElement('nav');nav.className='overlay-nav';nav.setAttribute('role','tablist');nav.setAttribute('aria-label','Layout type');for(const [id,label] of [['standard','Standard View layouts'],['automation','Automation layouts']]){const b=document.createElement('button');b.type='button';b.textContent=label;b.dataset.layoutTab=id;b.setAttribute('role','tab');b.onclick=()=>selectTab(id);nav.append(b);}page.append(nav);for(const id of ['standard','automation']){const panel=document.createElement('section');panel.id=id+'-layout-editor';panel.setAttribute('role','tabpanel');page.append(panel);}}
      standardWallDesigner.load(value);automationWallDesigner.load(value);selectTab(tab);
    }
  };
})();
