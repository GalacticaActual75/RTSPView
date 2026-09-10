const wallDesigner = (() => {
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
  function changed() { dirty = true; render(); message('Unsaved draft. Apply when ready to change the wall.'); }
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
    if (!validTile(candidate,index)) { render(); message('That tile overlaps another camera or extends beyond the grid.'); return; }
    current().tiles[index] = candidate; changed();
  }
  function preset(size) {
    const layout = current(), ids = config.cameras.map(camera => camera.slot);
    layout.rows = layout.columns = size === 'featured' ? 3 : Number(size);
    layout.tiles = size === 'featured'
      ? [{cameraSlot:ids[0],row:0,column:0,rowSpan:2,columnSpan:2},
         ...[[0,2],[1,2],[2,0],[2,1],[2,2]].map(([row,column],i) => ({cameraSlot:ids[i+1],row,column,rowSpan:1,columnSpan:1}))]
      : ids.slice(0,layout.rows*layout.columns).map((cameraSlot,i) => ({cameraSlot,row:Math.floor(i/layout.columns),column:i%layout.columns,rowSpan:1,columnSpan:1}));
    selectedTile = 0; changed();
  }
  function addCamera(slot, row, column) {
    const tile = {cameraSlot:slot,row,column,rowSpan:1,columnSpan:1};
    if (!validTile(tile,-1)) { message('Choose an empty cell and a camera not already in this layout.'); return; }
    current().tiles.push(tile); selectedTile = current().tiles.length-1; changed();
  }
  async function persist(apply = false, revert = false) {
    if (busy) return;
    const payload = revert ? copy(previous) : copy(draft);
    if (apply) payload.activeLayoutId = selectedId;
    // Saving edits to the active layout also changes its appearance. Require Apply for that case.
    if (!apply && !revert && JSON.stringify(payload.layouts.find(l=>l.id===saved.activeLayoutId)) !==
        JSON.stringify(saved.layouts.find(l=>l.id===saved.activeLayoutId))) {
      message('This draft edits the live layout. Use Apply, or duplicate it to save a separate layout.'); return;
    }
    busy = true; root.inert = true; message('Saving…');
    try {
      const result = await api('/api/layouts',{method:'PUT',body:JSON.stringify(payload)});
      previous = copy(saved); saved = copy(result); draft = copy(result); dirty = false;
      if (!draft.layouts.some(layout=>layout.id===selectedId)) selectedId = draft.activeLayoutId;
      render(); message(revert ? 'Previous saved layouts restored.' : apply ? 'Layout applied. The viewer will update shortly.' : 'Layouts saved.');
    } catch(error) { message(error.message); }
    finally { busy = false; root.inert = false; }
  }
  function render() {
    root.replaceChildren();
    const top = el('div',undefined,'designer-toolbar'); root.append(top);
    const layouts = el('select');
    for (const layout of draft.layouts) layouts.add(new Option(layout.name+(layout.id===saved.activeLayoutId?' • Live':''),layout.id));
    layouts.value = selectedId; layouts.onchange = () => {selectedId=layouts.value;selectedTile=0;render();};
    field('Saved layout',layouts,top);
    button('Duplicate',()=>{
      if(draft.layouts.length>=32){message('You can save up to 32 layouts.');return;}
      const layout=copy(current());layout.id='layout-'+Date.now().toString(36)+'-'+Math.random().toString(36).slice(2,8);
      layout.name=(layout.name+' copy').slice(0,80);draft.layouts.push(layout);selectedId=layout.id;changed();
    },top);
    button('Delete',()=>{
      if(selectedId===saved.activeLayoutId){message('Apply another layout before deleting the live layout.');return;}
      draft.layouts=draft.layouts.filter(layout=>layout.id!==selectedId);selectedId=draft.activeLayoutId;selectedTile=0;changed();
    },top);
    const layout=current(), name=el('input');name.value=layout.name;name.maxLength=80;
    name.oninput=()=>{layout.name=name.value.trim();dirty=true;message('Unsaved draft.');};field('Layout name',name,top);
    const presets=el('select');presets.add(new Option('Choose preset…',''));
    for(const [value,label] of [['1','1 × 1'],['2','2 × 2'],['3','3 × 3'],['4','4 × 4'],['featured','Featured camera']])presets.add(new Option(label,value));
    presets.onchange=()=>{if(presets.value)preset(presets.value);};field('Replace draft with preset',presets,top);
    for(const [key,label] of [['rows','Rows'],['columns','Columns']]){
      const input=el('input');input.type='number';input.min=1;input.max=4;input.value=layout[key];
      input.onchange=()=>{
        const value=Number(input.value),old=layout[key];layout[key]=value;
        if(!Number.isInteger(value)||value<1||value>4||layout.tiles.some((t,i)=>!validTile(t,i))){layout[key]=old;render();message('Remove or resize tiles before shrinking the grid.');return;}
        changed();
      };field(label,input,top);
    }
    root.append(el('p','Drag tiles to move them or swap equal-sized tiles. Drag the lower-right handle to resize. Select a tile for precise controls. Empty cells stay black. Previews use camera snapshots.','designer-help'));
    const hiddenOverlays=[config.doorbellOverlay,config.garageOverlay,...(config.additionalOverlays||[])].map(overlay=>[overlay.camera.name,overlay])
      .filter(([,overlay])=>overlay.camera.enabled&&!layout.tiles.some(tile=>tile.cameraSlot===overlay.hostCameraSlot));
    if(hiddenOverlays.length)root.append(el('p',hiddenOverlays.map(([name])=>name).join(' and ')+' will be hidden because the assigned camera is absent from this layout.','designer-help'));
    const workspace=el('div',undefined,'designer-workspace');root.append(workspace);
    board=el('div',undefined,'designer-board');board.setAttribute('aria-label','Camera wall layout preview');
    board.style.setProperty('--rows',layout.rows);board.style.setProperty('--columns',layout.columns);workspace.append(board);
    board.ondragover=e=>e.preventDefault();board.ondrop=e=>{e.preventDefault();const slot=Number(e.dataTransfer.getData('text/plain'));if(!config.cameras.some(c=>c.slot===slot))return;const bounds=board.getBoundingClientRect();addCamera(slot,Math.floor((e.clientY-bounds.top)/bounds.height*layout.rows),Math.floor((e.clientX-bounds.left)/bounds.width*layout.columns));};
    layout.tiles.forEach((tile,index)=>{
      const camera=config.cameras.find(camera=>camera.slot===tile.cameraSlot);
      const node=el('div',undefined,'designer-tile'+(index===selectedTile?' selected':''));node.tabIndex=0;
      node.setAttribute('aria-label',camera.name+'; row '+(tile.row+1)+', column '+(tile.column+1));
      Object.assign(node.style,{left:tile.column/layout.columns*100+'%',top:tile.row/layout.rows*100+'%',width:tile.columnSpan/layout.columns*100+'%',height:tile.rowSpan/layout.rows*100+'%'});
      const image=el('img');image.alt='';image.draggable=false;image.src='/api/cameras/'+tile.cameraSlot+'/thumbnail';image.onerror=()=>image.style.visibility='hidden';node.append(image);
      node.append(el('span',camera.name,'designer-caption'));
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
          node.classList.toggle('invalid',!validTile(candidate,index));
          Object.assign(node.style,{left:candidate.column/layout.columns*100+'%',top:candidate.row/layout.rows*100+'%',width:Math.max(1,candidate.columnSpan)/layout.columns*100+'%',height:Math.max(1,candidate.rowSpan)/layout.rows*100+'%'});
        };
        node.onpointerup=()=>{node.onpointermove=null;if(JSON.stringify(candidate)===JSON.stringify(tile))render();else updateTile(candidate,index);};
        node.onpointercancel=()=>render();
      };board.append(node);
    });
    const side=el('aside',undefined,'designer-inspector');workspace.append(side);
    const tile=layout.tiles[selectedTile];
    if(tile){
      side.append(el('h3','Selected tile'));
      const cameraSelect=el('select');for(const camera of config.cameras)cameraSelect.add(new Option(camera.name,camera.slot));cameraSelect.value=tile.cameraSlot;
      cameraSelect.onchange=()=>updateTile({...tile,cameraSlot:Number(cameraSelect.value)},selectedTile);field('Camera',cameraSelect,side);
      for(const [key,label,offset] of [['row','Row',1],['column','Column',1],['rowSpan','Height (cells)',0],['columnSpan','Width (cells)',0]]){
        const input=el('input');input.type='number';input.min=1;input.max=4;input.value=tile[key]+offset;
        input.onchange=()=>updateTile({...tile,[key]:Number(input.value)-offset},selectedTile);field(label,input,side);
      }
      button('Remove tile',()=>{if(layout.tiles.length===1){message('Keep at least one camera tile.');return;}layout.tiles.splice(selectedTile,1);selectedTile=0;changed();},side);
    }
    side.append(el('h3','Available cameras'),el('p','Drag onto an empty cell, or click to add.'));
    for(const camera of config.cameras.filter(camera=>!layout.tiles.some(t=>t.cameraSlot===camera.slot))){
      const node=button(camera.name,()=>{
        for(let row=0;row<layout.rows;row++)for(let column=0;column<layout.columns;column++)if(validTile({cameraSlot:camera.slot,row,column,rowSpan:1,columnSpan:1},-1)){addCamera(camera.slot,row,column);return;}
        message('No empty cell is available. Remove a tile or enlarge the grid.');
      },side);node.draggable=true;node.ondragstart=e=>e.dataTransfer.setData('text/plain',String(camera.slot));
    }
    const actions=el('div',undefined,'designer-actions');root.append(actions);
    button('Save layouts',()=>persist(),actions);
    button('Apply to wall',()=>persist(true),actions,false);
    button('Discard draft',()=>{draft=copy(saved);selectedId=saved.activeLayoutId;selectedTile=0;dirty=false;render();message('Draft discarded.');},actions);
    const revert=button('Revert last save',()=>persist(false,true),actions);revert.disabled=!previous;
    status=el('p',dirty?'Unsaved draft.':'Select a layout to edit or apply.','designer-status');status.setAttribute('role','status');root.append(status);
  }
  window.addEventListener('beforeunload',event=>{if(dirty){event.preventDefault();event.returnValue='';}});
  return {load(value){
    config=value;root=document.querySelector('#page-layouts');
    if(!root)return;
    saved=copy({layouts:value.layouts,activeLayoutId:value.activeLayoutId});
    if(!dirty){draft=copy(saved);selectedId=saved.activeLayoutId;selectedTile=0;}
    render();
  }};
})();
