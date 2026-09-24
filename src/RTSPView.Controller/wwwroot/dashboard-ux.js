/* Snapshot timestamps describe the displayed image, never the time it was fetched. */
const dashboardUX = (() => {
  const stamp = new WeakMap();
  const failed = new WeakSet();
  const resources=new Map();
  setInterval(()=>{for(const [image,url] of resources)if(!image.isConnected&&!image._ageLabel?.isConnected){URL.revokeObjectURL(url);resources.delete(image);}for(const image of resources.keys())caption(image);},10000);
  function age(image) {
    const date=stamp.get(image), seconds=date?Math.max(0,Math.floor((Date.now()-date)/1000)):null;
    if(failed.has(image))return resources.get(image)?'Preview unavailable · showing last image':'Preview unavailable';
    return seconds===null?'Preview · capture time unknown':`Preview · updated ${seconds<60?seconds+'s':Math.floor(seconds/60)+'m'} ago`;
  }
  function caption(image) {
    let label=image._ageLabel||image.nextElementSibling;
    if(!image._ageLabel&&!label?.classList.contains('snapshot-age')) {
      if(!image.parentElement)return;
      label=document.createElement('span');label.className='snapshot-age';image.after(label);
    }
    label.textContent=(image._agePrefix||'')+age(image);
    label.title=stamp.get(image)?'Snapshot captured '+new Date(stamp.get(image)).toLocaleString()+' · not live video':'Snapshot preview · not live video';
    const stale=!!stamp.get(image)&&Date.now()-stamp.get(image)>300000;
    label.classList.toggle('snapshot-warning',stale||failed.has(image)||!stamp.get(image));label.classList.toggle('stale',stale);if(stale)label.textContent+=' · Stale';
  }
  const attempted=new Map();let capturing=false;
  function visible(image) {
    const element=image._ageLabel||image;
    if(!element.isConnected||!element.getClientRects().length)return false;
    const rect=element.getBoundingClientRect();
    return rect.bottom>0&&rect.right>0&&rect.top<innerHeight&&rect.left<innerWidth;
  }
  async function refreshVisible() {
    if(document.hidden||capturing)return;
    const groups=new Map();
    for(const image of resources.keys())if(visible(image)) {
      const slot=Number(image.dataset.snapshotSlot);
      if(slot>0) {if(!groups.has(slot))groups.set(slot,[]);groups.get(slot).push(image);}
    }
    const next=[...groups].filter(([slot])=>Date.now()-(attempted.get(slot)||0)>=15000)
      .sort(([a],[b])=>(attempted.get(a)||0)-(attempted.get(b)||0))[0];
    if(!next)return;
    const [slot,images]=next;capturing=true;attempted.set(slot,Date.now());
    try {
      await api(`/api/cameras/${slot}/thumbnail/refresh`,{method:'POST'});
      if(!document.hidden)await Promise.all(images.filter(image=>visible(image)&&Number(image.dataset.snapshotSlot)===slot).map(image=>snapshot(image,slot)));
    } catch {
      for(const image of images)if(Number(image.dataset.snapshotSlot)===slot){failed.add(image);caption(image);}
    } finally {capturing=false;}
  }
  // One capture at a time, staggered across visible streams; no background-tab work.
  setInterval(refreshVisible,750);
  async function snapshot(image,slot) {
    if(!slot)return;
    if(!resources.has(image))resources.set(image,null);
    image.dataset.snapshotSlot=String(slot);const request={};image._snapshotRequest=request;
    try {
      const response=await fetch(`/api/cameras/${slot}/thumbnail?v=${Date.now()}`,{cache:'no-store'});
      if(!response.ok)throw new Error('Snapshot unavailable');
      const blob=await response.blob();if(image._snapshotRequest!==request)return;
      const url=URL.createObjectURL(blob),old=image.dataset.blobUrl;
      const date=Date.parse(response.headers.get('Last-Modified'));stamp.set(image,Number.isFinite(date)?date:null);
      image.src=url;image.dataset.blobUrl=url;resources.set(image,url);if(old)URL.revokeObjectURL(old);failed.delete(image);caption(image);
    } catch {if(image._snapshotRequest!==request)return;failed.add(image);caption(image);}
  }
  function sync() {
    const active=wallDesigner.active();
    for(const form of document.querySelectorAll('.camera-card')) {
      const slot=Number(form.dataset.slot),camera=cameraInventory.find(c=>c.slot===slot);
      if(form.dataset.appliedEnabled===undefined){form.dataset.appliedEnabled=String(form.elements.enabled.type==='checkbox'?form.elements.enabled.checked:form.elements.enabled.value==='true');form.dataset.appliedHost=form.elements.hostCameraSlot?.value||'';}
      if(camera) {
        const tile=active?.tiles.find(t=>t.cameraSlot===slot),duplicate=cameraInventory.filter(c=>c.name.trim().toLowerCase()===camera.name.trim().toLowerCase()).length>1;
        form.querySelector('.camera-position').textContent=(tile?`${active.name} · Row ${tile.row+1}, Column ${tile.column+1}`:camera.rtspUrl?'Configured but unassigned · Assign in Layouts':'No source configured · Edit stream')+` · #${slot}`+(duplicate?' · Duplicate name':'');
      }
    }
    for(const select of document.querySelectorAll('select[name="hostCameraSlot"]'))for(const option of select.options){const camera=cameraInventory.find(c=>c.slot===Number(option.value));if(camera)option.textContent=camera.name+' · #'+camera.slot;}
    workspace.sync();
  }
  function saved(camera) {if(camera.slot<10||camera.slot>25)automationUi.updateCamera(camera);const form=document.querySelector(`.camera-card[data-slot="${camera.slot}"]`);if(form){form.dataset.appliedEnabled=String(camera.enabled);form.dataset.appliedHost=form.elements.hostCameraSlot?.value||'';}const index=cameraInventory.findIndex(c=>c.slot===camera.slot);if(index>=0)cameraInventory[index]=camera;if(typeof overlaySourceUi!=='undefined')overlaySourceUi.refresh(cameraInventory);wallDesigner.updateCameras(cameraInventory);sync();}
  function health(t) {
    let cost=document.querySelector('#decoderCost');if(!cost){cost=document.createElement('p');cost.id='decoderCost';document.querySelector('#stats').after(cost);}
    const players=(t.viewer?.cameras||[]).filter(c=>c.configuredPlayer),hidden=players.filter(c=>!c.visible);
    cost.textContent=t.viewerConnected?players.length+' configured video players · '+hidden.length+' hidden but kept ready · '+(t.viewer?.cameras||[]).filter(c=>c.sharedDecoderSlot!=null).length+' original-source views sharing an overlay decoder. Hidden composited video skips uploads when all its views are hidden; decoding and network traffic continue.':'Video player counts unavailable while Viewer is disconnected.';
    for(const image of document.querySelectorAll('img[data-blob-url]'))caption(image);
  }
  function preview(form) {
    const dialog=document.createElement('dialog');dialog.className='snapshot-dialog';
    const title=document.createElement('h2');title.textContent=form.querySelector('.slot').textContent+' · #'+form.dataset.slot;
    const status=document.createElement('p');status.textContent=form.querySelector('.state').textContent;
    const image=document.createElement('img');image.alt=title.textContent;image.src=form.querySelector('.feed-thumbnail').src;stamp.set(image,stamp.get(form.querySelector('.feed-thumbnail')));
    const refresh=document.createElement('button');refresh.textContent='Refresh snapshot';refresh.onclick=async()=>{refresh.disabled=true;refresh.textContent='Capturing snapshot…';try{await api(`/api/cameras/${form.dataset.slot}/thumbnail/refresh`,{method:'POST'});await snapshot(image,form.dataset.slot);await snapshot(form.querySelector('.feed-thumbnail'),form.dataset.slot);}catch(error){status.textContent=error.message;}finally{refresh.disabled=false;refresh.textContent='Refresh snapshot';}};
    const close=document.createElement('button');close.className='secondary';close.textContent='Close';close.onclick=()=>dialog.close();
    const edit=document.createElement('button');edit.className='secondary';edit.textContent='Edit stream';edit.onclick=()=>{dialog.close();workspace.openStream(Number(form.dataset.slot));};dialog.setAttribute('aria-label',title.textContent);dialog.append(title,status,image,refresh,edit,close);document.body.append(dialog);caption(image);dialog.addEventListener('close',()=>{if(image.dataset.blobUrl)URL.revokeObjectURL(image.dataset.blobUrl);dialog.remove();});dialog.showModal();snapshot(image,form.dataset.slot);close.focus();
  }
  function trackDisplay() {
    const form=document.querySelector('#displayForm');let baseline=new FormData(form);const state=document.querySelector('#displayState');
    const key=data=>JSON.stringify([...data]);form.markSaved=()=>{baseline=new FormData(form);form.dataset.dirty='false';};
    form.oninput=()=>{const dirty=key(baseline)!==key(new FormData(form));form.dataset.dirty=String(dirty);state.textContent=dirty?'Unsaved changes — Apply changes updates the wall':'Applied';};
  }
  return {snapshot,sync,saved,health,preview,trackDisplay,age};
})();
