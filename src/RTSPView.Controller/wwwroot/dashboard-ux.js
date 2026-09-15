/* Snapshot timestamps describe the displayed image, never the time it was fetched. */
const dashboardUX = (() => {
  const styles=document.createElement('link');styles.rel='stylesheet';styles.href='dashboard.css?v=1';document.head.append(styles);
  document.addEventListener('DOMContentLoaded',()=>document.head.append(styles),{once:true});
  const stamp = new WeakMap();
  const failed = new WeakSet();
  const resources=new Map();
  setInterval(()=>{for(const [image,url] of resources)if(!image.isConnected&&!image._ageLabel?.isConnected){URL.revokeObjectURL(url);resources.delete(image);}for(const image of resources.keys())caption(image);},10000);
  function age(image) {
    const date=stamp.get(image), seconds=date?Math.max(0,Math.floor((Date.now()-date)/1000)):null;
    return failed.has(image)?'Snapshot refresh failed · '+(seconds===null?'age unknown':Math.floor(seconds/60)+' minutes old'):seconds===null?'Snapshot age unknown':`Snapshot captured ${seconds<60?seconds+' seconds':Math.floor(seconds/60)+' minutes'} ago${seconds>=300?' · Stale':''}`;
  }
  function caption(image) {
    if(image._ageLabel){image._ageLabel.textContent=(image._agePrefix||'')+age(image);return;}
    if(!image.parentElement)return;
    let label=image.nextElementSibling;
    if(!label?.classList.contains('snapshot-age')) {label=document.createElement('span');label.className='snapshot-age';image.after(label);}
    label.textContent=age(image);label.title=stamp.get(image)?new Date(stamp.get(image)).toLocaleString():'No capture timestamp available';label.classList.toggle('stale',!stamp.get(image)||Date.now()-stamp.get(image)>=300000);
  }
  async function snapshot(image,slot) {
    if(!slot)return;
    image.dataset.snapshotSlot=String(slot);const request={};image._snapshotRequest=request;
    try {
      const response=await fetch(`/api/cameras/${slot}/thumbnail?v=${Date.now()}`,{cache:'no-store'});
      if(!response.ok)throw new Error('Snapshot unavailable');
      const blob=await response.blob();if(image._snapshotRequest!==request)return;
      const url=URL.createObjectURL(blob),old=image.dataset.blobUrl;
      const date=Date.parse(response.headers.get('Last-Modified'));stamp.set(image,Number.isFinite(date)?date:null);
      image.src=url;image.dataset.blobUrl=url;resources.set(image,url);if(old)URL.revokeObjectURL(old);failed.delete(image);caption(image);
    } catch {if(image._snapshotRequest!==request)return;failed.add(image);caption(image);const label=image._ageLabel||image.nextElementSibling;if(label)label.textContent=image.src?age(image):'Snapshot unavailable';}
  }
  function sync() {
    const active=wallDesigner.active();
    for(const form of document.querySelectorAll('.camera-card')) {
      const slot=Number(form.dataset.slot),camera=cameraInventory.find(c=>c.slot===slot);
      if(form.dataset.appliedEnabled===undefined){form.dataset.appliedEnabled=String(form.elements.enabled.type==='checkbox'?form.elements.enabled.checked:form.elements.enabled.value==='true');form.dataset.appliedHost=form.elements.hostCameraSlot?.value||'';}
      if(camera) {
        const tile=active?.tiles.find(t=>t.cameraSlot===slot),duplicate=cameraInventory.filter(c=>c.name.trim().toLowerCase()===camera.name.trim().toLowerCase()).length>1;
        form.querySelector('.camera-position').textContent=(tile?`${active.name} · Row ${tile.row+1}, Column ${tile.column+1}`:'Unassigned · Assign to layout')+` · #${slot}`+(duplicate?' · Duplicate name':'');
      }
    }
    for(const select of document.querySelectorAll('select[name="hostCameraSlot"]'))for(const option of select.options){const camera=cameraInventory.find(c=>c.slot===Number(option.value));if(camera)option.textContent=camera.name+' · #'+camera.slot;}
  }
  function saved(camera) {const form=document.querySelector(`.camera-card[data-slot="${camera.slot}"]`);if(form){form.dataset.appliedEnabled=String(camera.enabled);form.dataset.appliedHost=form.elements.hostCameraSlot?.value||'';}const index=cameraInventory.findIndex(c=>c.slot===camera.slot);if(index>=0)cameraInventory[index]=camera;wallDesigner.updateCameras(cameraInventory);sync();}
  function health(t) {
    let summary=document.querySelector('#cameraHealth');if(!summary){summary=document.createElement('section');summary.id='cameraHealth';summary.className='panel health-summary';document.querySelector('#stats').before(summary);}
    const enabled=cameraInventory.filter(c=>c.enabled),connected=enabled.filter(c=>t.viewerConnected&&t.viewer?.cameras.some(v=>v.slot===c.slot&&v.state==='Live')).length;
    const active=wallDesigner.active(),overlays=[...document.querySelectorAll('.camera-card')].filter(f=>f.dataset.kind!=='camera'&&f.dataset.appliedEnabled==='true'&&active?.tiles.some(tile=>tile.cameraSlot===Number(f.dataset.appliedHost))&&t.viewerConnected&&t.viewer?.cameras.some(c=>c.slot===Number(f.dataset.slot)&&c.state==='Live'&&!c.frameWarning)).length;
    const problems=enabled.filter(c=>!t.viewerConnected||!t.viewer?.cameras.some(v=>v.slot===c.slot&&v.state==='Live'&&!v.frameWarning)).length;
    const stale=[...document.querySelectorAll('#cameras .feed-thumbnail')].filter(image=>!stamp.get(image)||Date.now()-stamp.get(image)>=300000).length;
    summary.textContent=`${connected}/${enabled.length} streams connected · Active layout: ${active?.name||'None'} · ${overlays} connected always-visible overlays · ${problems} streams need attention · ${stale} stale or undated snapshots`;
    for(const image of document.querySelectorAll('img[data-blob-url]'))caption(image);
  }
  function preview(form) {
    const dialog=document.createElement('dialog');dialog.className='snapshot-dialog';
    const title=document.createElement('h2');title.textContent=form.querySelector('.slot').textContent+' · #'+form.dataset.slot;
    const status=document.createElement('p');status.textContent=form.querySelector('.state').textContent;
    const image=document.createElement('img');image.alt=title.textContent;image.src=form.querySelector('.feed-thumbnail').src;stamp.set(image,stamp.get(form.querySelector('.feed-thumbnail')));
    const refresh=document.createElement('button');refresh.textContent='Refresh snapshot';refresh.onclick=async()=>{refresh.disabled=true;refresh.textContent='Capturing snapshot…';try{await api(`/api/cameras/${form.dataset.slot}/thumbnail/refresh`,{method:'POST'});await snapshot(image,form.dataset.slot);await snapshot(form.querySelector('.feed-thumbnail'),form.dataset.slot);}catch(error){status.textContent=error.message;}finally{refresh.disabled=false;refresh.textContent='Refresh snapshot';}};
    const close=document.createElement('button');close.className='secondary';close.textContent='Close';close.onclick=()=>dialog.close();
    dialog.append(title,status,image,refresh,close);document.body.append(dialog);caption(image);dialog.addEventListener('close',()=>{if(image.dataset.blobUrl)URL.revokeObjectURL(image.dataset.blobUrl);dialog.remove();});dialog.showModal();close.focus();
  }
  function trackDisplay() {
    const form=document.querySelector('#displayForm');let baseline=new FormData(form);const state=document.querySelector('#displayState');
    const key=data=>JSON.stringify([...data]);form.markSaved=()=>{baseline=new FormData(form);form.dataset.dirty='false';};
    form.oninput=()=>{const dirty=key(baseline)!==key(new FormData(form));form.dataset.dirty=String(dirty);state.textContent=dirty?'Unsaved changes — Apply changes updates the wall':'Applied';};
  }
  return {snapshot,sync,saved,health,preview,trackDisplay,age};
})();
