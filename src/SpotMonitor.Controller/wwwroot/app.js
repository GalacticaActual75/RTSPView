const q=s=>document.querySelector(s),login=q('#login'),app=q('#app'),grid=q('#cameras'),doorbellGrid=q('#doorbell'),garageGrid=q('#garage');let csrfToken='';
const brand=q('.brand');if(brand){const brandIcon=document.createElement('img');brandIcon.src='favicon-32.png';brandIcon.alt='';brandIcon.className='brand-icon';brand.prepend(brandIcon)}
const compactStyles=document.createElement('link');compactStyles.rel='stylesheet';compactStyles.href='compact.css?v=14.0';document.head.appendChild(compactStyles);
q('#passwordForm').style.marginTop='38px';
function combineViewerControls(){const remote=document.querySelector('main > .control-panel.panel'),display=q('#displayForm'),updates=q('#updatePanel'),options=display?.querySelector('.display-options');if(!remote||!display||!updates||!options)return;remote.classList.add('viewer-display-panel');remote.querySelector('h2').textContent='Viewer and display';remote.querySelector('p').textContent='Immediate viewer controls and persistent wall behavior.';const viewerButtons=remote.querySelector('.control-buttons');viewerButtons.classList.add('viewer-action-strip');updates.classList.remove('panel','control-panel','bottom-panel');updates.classList.add('viewer-update-section');remote.insertBefore(updates,viewerButtons);display.classList.remove('panel');display.classList.add('combined-display');display.querySelector('h2').textContent='Wall behavior';display.querySelector('div>p').textContent='Choose how the viewer protects and presents the camera wall.';const alwaysOnTop=document.createElement('label');alwaysOnTop.innerHTML='<input name="keepViewerAlwaysOnTop" type="checkbox"> Keep viewer always on top';options.appendChild(alwaysOnTop);const toggles=document.createElement('div'),fields=document.createElement('div');toggles.className='display-toggle-grid';fields.className='display-field-row';for(const label of [...options.querySelectorAll(':scope > label')]){const input=label.querySelector('input');(input?.type==='checkbox'?toggles:fields).appendChild(label)}const actions=display.querySelector('.actions');fields.appendChild(actions);options.replaceWith(toggles,fields);const paragraphs=display.querySelectorAll(':scope > p');if(paragraphs.length)paragraphs[paragraphs.length-1].classList.add('display-note');remote.appendChild(display)}
combineViewerControls();
async function api(url,options={}){const method=(options.method||'GET').toUpperCase(),headers={'Content-Type':'application/json',...(options.headers||{})};if(!['GET','HEAD'].includes(method)&&csrfToken)headers['X-CSRF-Token']=csrfToken;const r=await fetch(url,{headers,...options});if(r.status===401){showLogin();throw new Error('Authentication required')}if(!r.ok){const e=await r.json().catch(()=>({error:r.statusText}));throw new Error(e.error||r.statusText)}return r.status===204?null:r.json()}
function showLogin(){login.hidden=false;app.hidden=true}function showApp(){login.hidden=true;app.hidden=false}
q('#loginForm').onsubmit=async e=>{e.preventDefault();q('#loginError').textContent='';try{await api('/api/auth/login',{method:'POST',body:JSON.stringify({password:q('#password').value})});showApp();await load()}catch(err){q('#loginError').textContent=err.message}};
q('#logout').onclick=async()=>{await api('/api/auth/logout',{method:'POST'});showLogin()};
function value(form,name){const el=form.elements[name];return el.type==='checkbox'?el.checked:(el.type==='number'||el.tagName==='SELECT'||el.dataset.number==='true')?Number(el.value):el.value}
function rangeField(label,name,min,max,step=1){return `<label class="overlay-range-field"><span>${label}</span><span class="range-input-pair"><input type="range" min="${min}" max="${max}" step="${step}" data-sync="${name}" aria-label="${label} slider"><input name="${name}" type="number" min="${min}" max="${max}" step="${step}" aria-label="${label} value"></span></label>`}
function bindRangeFields(form,root){for(const range of root.querySelectorAll('input[type="range"][data-sync]')){const number=form.elements[range.dataset.sync];range.value=number.value;range.addEventListener('input',()=>number.value=range.value);number.addEventListener('input',()=>{if(number.value!=='')range.value=number.value})}}
function refreshThumbnail(image){image.src=`/api/cameras/${image.dataset.slot}/thumbnail?v=${Date.now()}`}
function validCustomPathData(pathData){return pathData.length>0&&pathData.length<=65536&&/^[0-9eE+.,\s\-MmZzLlHhVvCcSsQqTtAa]+$/.test(pathData)&&/[Mm]/.test(pathData)}
async function extractCustomViewportSvg(file){
 if(!file)throw new Error('Choose an SVG file.');
 if(file.size>262144)throw new Error('SVG files must be 256 KB or smaller.');
 const text=await file.text();
 if(/<!DOCTYPE|<!ENTITY/i.test(text))throw new Error('SVG document types and entities are not allowed.');
 const documentNode=new DOMParser().parseFromString(text,'image/svg+xml'),root=documentNode.documentElement;
 if(root.localName!=='svg'||documentNode.querySelector('parsererror'))throw new Error('The selected file is not valid SVG.');
 const viewBox=(root.getAttribute('viewBox')||'').trim().split(/[\s,]+/).map(Number);
 if(viewBox.length!==4||!viewBox.every(Number.isFinite)||viewBox[2]<=0||viewBox[3]<=0)throw new Error('The SVG must define a valid viewBox.');
 const pathSources=[...root.querySelectorAll('path')].filter(path=>!path.closest('clipPath,defs,mask,pattern,marker,symbol'));
 if(!pathSources.length)throw new Error('The SVG must contain at least one path. Convert shapes and text to paths before uploading.');
 const paths=pathSources.map(path=>{
  if(path.closest('[transform]'))throw new Error('Transformed SVG paths are not supported. Flatten transforms before uploading.');
  const data=(path.getAttribute('d')||'').trim();
  if(!validCustomPathData(data))throw new Error('The SVG contains unsupported or overly complex path data.');
  try{new Path2D(data)}catch{throw new Error('The SVG path could not be interpreted.');}
  return data;
 });
 const namespace='http://www.w3.org/2000/svg',measurer=document.createElementNS(namespace,'svg');
 measurer.setAttribute('viewBox',viewBox.join(' '));measurer.setAttribute('width',String(Math.max(1,viewBox[2])));measurer.setAttribute('height',String(Math.max(1,viewBox[3])));
 Object.assign(measurer.style,{position:'fixed',left:'-100000px',top:'-100000px',opacity:'0',pointerEvents:'none'});document.body.appendChild(measurer);
 try{
  const measured=paths.map(data=>{const path=document.createElementNS(namespace,'path');path.setAttribute('d',data);path.setAttribute('fill','#000');measurer.appendChild(path);return{data,bounds:path.getBBox()}}).filter(item=>item.bounds.width>0&&item.bounds.height>0);
  if(!measured.length)throw new Error('The SVG path has no usable area.');
  const [viewX,viewY,viewWidth,viewHeight]=viewBox,toleranceX=viewWidth*.01,toleranceY=viewHeight*.01;
  const isCanvasBackground=item=>item.bounds.x<=viewX+toleranceX&&item.bounds.y<=viewY+toleranceY&&item.bounds.width>=viewWidth*.98&&item.bounds.height>=viewHeight*.98;
  let selected=measured.length>1?measured.filter(item=>!isCanvasBackground(item)):measured;if(!selected.length)selected=measured;
  const group=document.createElementNS(namespace,'g');measurer.replaceChildren(group);for(const item of selected){const path=document.createElementNS(namespace,'path');path.setAttribute('d',item.data);path.setAttribute('fill','#000');group.appendChild(path)}
  const bounds=group.getBBox(),pathData=selected.map(item=>item.data).join(' ');
  if(!validCustomPathData(pathData)||!Number.isFinite(bounds.x)||!Number.isFinite(bounds.y)||bounds.width<=0||bounds.height<=0)throw new Error('The SVG path could not be normalized.');
  const clean=number=>Number(number.toFixed(6));
  return{sourceName:file.name,pathData,viewBoxX:clean(bounds.x),viewBoxY:clean(bounds.y),viewBoxWidth:clean(bounds.width),viewBoxHeight:clean(bounds.height)};
 }finally{measurer.remove()}
}
function createOverlayPreview(form,overlayDefinition){
 const overlayLabel=overlayDefinition.label,overlaySlot=overlayDefinition.slot,defaultHostSlot=overlayDefinition.defaultHostSlot;
 const panel=document.createElement('section');
 panel.className='doorbell-preview-panel';
 panel.innerHTML=`<div class="doorbell-preview-head"><div><h3>Wall preview</h3><p>Drag the viewport to place it on the host camera.</p></div><div class="doorbell-preview-actions"><div class="preview-mode-switch"><button type="button" class="secondary active" data-preview-mode="wall">Wall preview</button><button type="button" class="secondary" data-preview-mode="source">Source framing</button></div><button type="button" class="secondary refresh-doorbell-preview">Refresh images</button></div></div><div class="doorbell-preview-stage"><canvas aria-label="Interactive ${overlayLabel} viewport preview"></canvas></div><p class="doorbell-preview-help">Drag the viewport to position it on the wall.</p>`;
 const canvas=panel.querySelector('canvas'),context=canvas.getContext('2d'),hostImage=new Image(),overlayImage=new Image(),thumbnail=form.querySelector('.feed-thumbnail');
 const heading=panel.querySelector('.doorbell-preview-head h3'),description=panel.querySelector('.doorbell-preview-head p'),help=panel.querySelector('.doorbell-preview-help');
 let drawQueued=false,mode='wall',dragging=false,lastLayout=null;
 const number=(name,fallback)=>{const raw=form.elements[name]?.value,parsed=Number(raw);return raw!==''&&Number.isFinite(parsed)?parsed:fallback};
 const clamp=(value,min,max)=>Math.min(max,Math.max(min,value));
 const loadHost=()=>hostImage.src='/api/cameras/'+number('hostCameraSlot',defaultHostSlot)+'/thumbnail?v='+Date.now();
 const loadOverlay=()=>overlayImage.src=thumbnail.src||'/api/cameras/'+overlaySlot+'/thumbnail?v='+Date.now();
 const scheduleDraw=()=>{if(drawQueued)return;drawQueued=true;requestAnimationFrame(()=>{drawQueued=false;draw()})};
 function setSynced(name,newValue){
  const numeric=form.elements[name],newNumber=Math.round(clamp(newValue,Number(numeric.min),Number(numeric.max)));
  numeric.value=newNumber;
  const slider=form.querySelector('input[type="range"][data-sync="'+name+'"]');
  if(slider)slider.value=newNumber;
  numeric.dispatchEvent(new Event('input',{bubbles:true}));
 }
 function viewportGeometry(width,height){
  const shape=number('viewportShape',0);
  let viewportWidth=width*clamp(number('viewportWidthPercent',50),10,95)/100;
  let viewportHeight=height*clamp(number('viewportHeightPercent',50),10,95)/100;
  if(shape===1||shape===3)viewportWidth=viewportHeight=Math.min(viewportWidth,viewportHeight);
  const travelX=Math.max(0,width-viewportWidth),travelY=Math.max(0,height-viewportHeight);
  return {shape,width:viewportWidth,height:viewportHeight,left:travelX*clamp(number('viewportHorizontalPositionPercent',0),0,100)/100,top:travelY*clamp(number('viewportVerticalPositionPercent',100),0,100)/100,travelX,travelY};
 }
 function sourceCrop(sourceWidth,sourceHeight,displayWidth,displayHeight){
  sourceWidth=Math.max(1,sourceWidth);sourceHeight=Math.max(1,sourceHeight);displayWidth=Math.max(1,displayWidth);displayHeight=Math.max(1,displayHeight);
  const zoom=clamp(number('zoomPercent',100),100,300)/100;
  const scale=Math.max(displayWidth/sourceWidth,displayHeight/sourceHeight)*zoom;
  const cropWidth=displayWidth/scale,cropHeight=displayHeight/scale,slackX=sourceWidth-cropWidth,slackY=sourceHeight-cropHeight;
  return {x:slackX*clamp(number('imageHorizontalPositionPercent',50),0,100)/100,y:slackY*clamp(number('imageVerticalPositionPercent',50),0,100)/100,width:cropWidth,height:cropHeight,slackX,slackY};
 }
 function imageCover(image,x,y,width,height){const scale=Math.max(width/image.naturalWidth,height/image.naturalHeight),sourceWidth=width/scale,sourceHeight=height/scale,sourceX=(image.naturalWidth-sourceWidth)/2,sourceY=(image.naturalHeight-sourceHeight)/2;context.drawImage(image,sourceX,sourceY,sourceWidth,sourceHeight,x,y,width,height)}
 function viewportPath(shape,x,y,width,height){
  const path=new Path2D();
  if(shape===5){
   const data=form.elements.customViewportPathData?.value||'',viewX=number('customViewportViewBoxX',0),viewY=number('customViewportViewBoxY',0),viewWidth=number('customViewportViewBoxWidth',1),viewHeight=number('customViewportViewBoxHeight',1);
   if(validCustomPathData(data)&&viewWidth>0&&viewHeight>0){try{const rotation=clamp(number('customViewportRotationDegrees',0),-180,180),transform=new DOMMatrix().translate(x+width/2,y+height/2).rotate(rotation).translate(-width/2,-height/2).scale(width/viewWidth,height/viewHeight).translate(-viewX,-viewY);path.addPath(new Path2D(data),transform);return{path,fillRule:'evenodd'}}catch{}}
  }
  if(shape===3||shape===4)path.ellipse(x+width/2,y+height/2,width/2,height/2,0,0,Math.PI*2);
  else if(shape===2){const radius=Math.min(width,height)*.22;path.moveTo(x+radius,y);path.lineTo(x+width-radius,y);path.quadraticCurveTo(x+width,y,x+width,y+radius);path.lineTo(x+width,y+height-radius);path.quadraticCurveTo(x+width,y+height,x+width-radius,y+height);path.lineTo(x+radius,y+height);path.quadraticCurveTo(x,y+height,x,y+height-radius);path.lineTo(x,y+radius);path.quadraticCurveTo(x,y,x+radius,y)}
  else path.rect(x,y,width,height);
  path.closePath();return{path,fillRule:'nonzero'};
 }
 function prepareCanvas(){
  const width=Math.max(320,Math.round(canvas.getBoundingClientRect().width||800)),height=Math.round(width*9/16),ratio=Math.min(window.devicePixelRatio||1,2);
  if(canvas.width!==Math.round(width*ratio)||canvas.height!==Math.round(height*ratio)){canvas.width=Math.round(width*ratio);canvas.height=Math.round(height*ratio)}
  context.setTransform(ratio,0,0,ratio,0,0);context.clearRect(0,0,width,height);context.fillStyle='#05070a';context.fillRect(0,0,width,height);
  return {width,height};
 }
 function drawWall(width,height){
  if(hostImage.complete&&hostImage.naturalWidth)imageCover(hostImage,0,0,width,height);
  else{context.fillStyle='#151b22';context.fillRect(0,0,width,height);context.fillStyle='#8fa0b3';context.font='14px Segoe UI';context.fillText('Host camera snapshot unavailable',20,30)}
  const viewport=viewportGeometry(width,height),source=sourceCrop(overlayImage.naturalWidth||4,overlayImage.naturalHeight||3,viewport.width,viewport.height);
  const viewportClip=viewportPath(viewport.shape,viewport.left,viewport.top,viewport.width,viewport.height),opacity=clamp(number('viewportOpacityPercent',100),20,100)/100;context.save();context.globalAlpha=opacity;context.save();context.clip(viewportClip.path,viewportClip.fillRule);
  if(overlayImage.complete&&overlayImage.naturalWidth)context.drawImage(overlayImage,source.x,source.y,source.width,source.height,viewport.left,viewport.top,viewport.width,viewport.height);
  else{context.fillStyle='#111820';context.fillRect(viewport.left,viewport.top,viewport.width,viewport.height);context.fillStyle='#d2dae4';context.font='13px Segoe UI';context.fillText(overlayLabel+' snapshot unavailable',viewport.left+12,viewport.top+24)}
  context.restore();context.strokeStyle='rgba(0,0,0,.92)';context.lineWidth=3;context.stroke(viewportClip.path);context.restore();
  lastLayout={mode,viewport,canvasWidth:width,canvasHeight:height};
 }
 function drawSource(width,height){
  if(!overlayImage.complete||!overlayImage.naturalWidth){context.fillStyle='#151b22';context.fillRect(0,0,width,height);context.fillStyle='#8fa0b3';context.font='14px Segoe UI';context.fillText(overlayLabel+' snapshot unavailable',20,30);lastLayout=null;return}
  const scale=Math.min(width/overlayImage.naturalWidth,height/overlayImage.naturalHeight),imageWidth=overlayImage.naturalWidth*scale,imageHeight=overlayImage.naturalHeight*scale,imageLeft=(width-imageWidth)/2,imageTop=(height-imageHeight)/2;
  context.drawImage(overlayImage,imageLeft,imageTop,imageWidth,imageHeight);
  const viewport=viewportGeometry(width,height),source=sourceCrop(overlayImage.naturalWidth,overlayImage.naturalHeight,viewport.width,viewport.height);
  const selection={left:imageLeft+source.x*scale,top:imageTop+source.y*scale,width:source.width*scale,height:source.height*scale};
  context.fillStyle='rgba(0,0,0,.62)';
  context.fillRect(imageLeft,imageTop,imageWidth,Math.max(0,selection.top-imageTop));
  context.fillRect(imageLeft,selection.top+selection.height,imageWidth,Math.max(0,imageTop+imageHeight-selection.top-selection.height));
  context.fillRect(imageLeft,selection.top,Math.max(0,selection.left-imageLeft),selection.height);
  context.fillRect(selection.left+selection.width,selection.top,Math.max(0,imageLeft+imageWidth-selection.left-selection.width),selection.height);
  const selectionPath=viewportPath(viewport.shape,selection.left,selection.top,selection.width,selection.height);context.strokeStyle='#3e9bff';context.lineWidth=3;context.stroke(selectionPath.path);
  context.fillStyle='rgba(4,12,20,.78)';context.fillRect(selection.left+8,selection.top+8,118,25);context.fillStyle='#fff';context.font='600 12px Segoe UI';context.fillText('VISIBLE ON WALL',selection.left+17,selection.top+25);
  lastLayout={mode,source,scale,imageLeft,imageTop,canvasWidth:width,canvasHeight:height};
 }
 function draw(){const size=prepareCanvas();if(mode==='source')drawSource(size.width,size.height);else drawWall(size.width,size.height)}
 function updateFromPointer(event){
  if(!lastLayout)return;
  const rect=canvas.getBoundingClientRect(),x=(event.clientX-rect.left)*lastLayout.canvasWidth/rect.width,y=(event.clientY-rect.top)*lastLayout.canvasHeight/rect.height;
  if(mode==='wall'){
   const viewport=lastLayout.viewport;
   if(viewport.travelX>0)setSynced('viewportHorizontalPositionPercent',(x-viewport.width/2)/viewport.travelX*100);
   if(viewport.travelY>0)setSynced('viewportVerticalPositionPercent',(y-viewport.height/2)/viewport.travelY*100);
  }else{
   const source=lastLayout.source,sourceX=(x-lastLayout.imageLeft)/lastLayout.scale,sourceY=(y-lastLayout.imageTop)/lastLayout.scale;
   if(source.slackX>0)setSynced('imageHorizontalPositionPercent',(sourceX-source.width/2)/source.slackX*100);
   if(source.slackY>0)setSynced('imageVerticalPositionPercent',(sourceY-source.height/2)/source.slackY*100);
  }
 }
 function selectMode(nextMode){
  mode=nextMode;
  for(const button of panel.querySelectorAll('[data-preview-mode]'))button.classList.toggle('active',button.dataset.previewMode===mode);
  if(mode==='source'){heading.textContent='Source framing';description.textContent='The full '+overlayLabel+' image stays visible. Drag the blue selection to choose the area shown on the wall.';help.textContent='Everything outside the blue shape is hidden. Zoom changes its size; drag to pan it anywhere in the source image.'}
  else{heading.textContent='Wall preview';description.textContent='Unsaved changes use the same framing calculation as the live wall.';help.textContent='Drag the viewport to place it on the host camera. Its image remains aspect-preserved with no black bars.'}
  scheduleDraw();
 }
 for(const button of panel.querySelectorAll('[data-preview-mode]'))button.onclick=()=>selectMode(button.dataset.previewMode);
 canvas.addEventListener('pointerdown',event=>{dragging=true;canvas.setPointerCapture(event.pointerId);updateFromPointer(event)});
 canvas.addEventListener('pointermove',event=>{if(dragging)updateFromPointer(event)});
 canvas.addEventListener('pointerup',()=>dragging=false);
 canvas.addEventListener('pointercancel',()=>dragging=false);
 hostImage.onload=scheduleDraw;hostImage.onerror=scheduleDraw;overlayImage.onload=scheduleDraw;overlayImage.onerror=scheduleDraw;
 thumbnail.addEventListener('load',loadOverlay);
 form.addEventListener('input',scheduleDraw);
 form.addEventListener('change',event=>{if(event.target.name==='hostCameraSlot')loadHost();scheduleDraw()});
 const refreshButton=panel.querySelector('.refresh-doorbell-preview');
 refreshButton.onclick=async()=>{const originalText=refreshButton.textContent;refreshButton.disabled=true;refreshButton.textContent='Refreshing...';try{await Promise.all([api('/api/cameras/'+number('hostCameraSlot',defaultHostSlot)+'/thumbnail/refresh',{method:'POST'}),api('/api/cameras/'+overlaySlot+'/thumbnail/refresh',{method:'POST'})]);loadHost();refreshThumbnail(thumbnail);refreshButton.textContent='Refreshed'}catch(error){refreshButton.textContent='Refresh failed';console.error(error)}finally{setTimeout(()=>{refreshButton.textContent=originalText;refreshButton.disabled=false},1200)}};
 if('ResizeObserver'in window)new ResizeObserver(scheduleDraw).observe(panel.querySelector('.doorbell-preview-stage'));else window.addEventListener('resize',scheduleDraw);
 loadHost();loadOverlay();selectMode('wall');return panel
}
function bindCustomViewportUpload(form,placement){
 const shape=form.elements.viewportShape,container=placement.querySelector('.custom-viewport-upload'),fileInput=placement.querySelector('.custom-viewport-file'),status=placement.querySelector('.custom-viewport-status'),removeButton=placement.querySelector('.remove-custom-viewport');
 const setValue=(name,newValue)=>form.elements[name].value=newValue;
 function sync(){const hasPath=validCustomPathData(form.elements.customViewportPathData.value),selected=Number(shape.value)===5;container.hidden=!selected;status.classList.remove('error');status.textContent=hasPath?(form.elements.customViewportSourceName.value||'Custom SVG'):'No custom SVG uploaded';removeButton.hidden=!hasPath}
 shape.addEventListener('change',sync);
 fileInput.addEventListener('change',async()=>{const file=fileInput.files?.[0];if(!file)return;status.classList.remove('error');status.textContent='Reading SVG...';fileInput.disabled=true;try{const custom=await extractCustomViewportSvg(file);setValue('customViewportSourceName',custom.sourceName);setValue('customViewportPathData',custom.pathData);setValue('customViewportViewBoxX',custom.viewBoxX);setValue('customViewportViewBoxY',custom.viewBoxY);setValue('customViewportViewBoxWidth',custom.viewBoxWidth);setValue('customViewportViewBoxHeight',custom.viewBoxHeight);shape.value='5';sync();form.dispatchEvent(new Event('input',{bubbles:true}))}catch(error){status.classList.add('error');status.textContent=error.message}finally{fileInput.disabled=false;fileInput.value=''}});
 removeButton.addEventListener('click',()=>{setValue('customViewportSourceName','');setValue('customViewportPathData','');setValue('customViewportViewBoxX',0);setValue('customViewportViewBoxY',0);setValue('customViewportViewBoxWidth',1);setValue('customViewportViewBoxHeight',1);if(Number(shape.value)===5)shape.value='0';sync();shape.dispatchEvent(new Event('change',{bubbles:true}))});
 sync();return sync
}
function cameraCard(camera,overlay=null,overlayDefinition=null){
 const overlayMode=overlay!==null;
 const overlayLabel=overlayDefinition?.label||'';
 const form=q('#cameraTemplate').content.firstElementChild.cloneNode(true),thumbnail=form.querySelector('.feed-thumbnail'),actions=form.querySelector('.actions');
 form.dataset.slot=camera.slot;form.dataset.kind=overlayMode?overlayDefinition.kind:'camera';form.dataset.label=overlayMode?overlayLabel:`Camera ${camera.slot}`;form.querySelector('.slot').textContent=overlayMode?`${overlayLabel} overlay`:`Camera ${camera.slot}`;thumbnail.dataset.slot=camera.slot;
 thumbnail.onload=()=>thumbnail.style.visibility='visible';thumbnail.onerror=()=>thumbnail.style.visibility='hidden';refreshThumbnail(thumbnail);
 for(const [key,val]of Object.entries(camera)){const el=form.elements[key];if(!el)continue;if(el.type==='checkbox')el.checked=val;else el.value=val}
 if(!overlayMode){
  const mover=document.createElement('div');mover.className='two camera-mover';mover.innerHTML=`<label>Move to position<select>${Array.from({length:9},(_,i)=>`<option value="${i+1}"${i+1===camera.slot?' selected':''}>Camera ${i+1}</option>`).join('')}</select></label><button type="button" class="secondary">Move / swap</button>`;
  const moveButton=mover.querySelector('button');moveButton.style.alignSelf='end';moveButton.style.marginBottom='10px';moveButton.onclick=async()=>{const toSlot=Number(mover.querySelector('select').value);if(toSlot===camera.slot)return;if(!confirm(`Move Camera ${camera.slot} to position ${toSlot}? The two camera positions will be swapped.`))return;const state=form.querySelector('.save-state');state.textContent='Moving...';try{await api('/api/cameras/reorder',{method:'POST',body:JSON.stringify({fromSlot:camera.slot,toSlot})});await load()}catch(err){state.textContent=err.message}};
  form.insertBefore(mover,actions);
 }
 const head=form.querySelector('.card-head'),slotLabel=form.querySelector('.slot'),identity=document.createElement('div'),position=document.createElement('span');identity.className='camera-identity';position.className='camera-position';slotLabel.textContent=camera.name||`Camera ${camera.slot}`;position.textContent=`Position ${camera.slot}`;identity.append(slotLabel,position);head.insertBefore(identity,head.firstChild);
 if(overlayMode)position.textContent='Picture-in-picture stream';
 const live=form.querySelector('.camera-live'),thumbnailSource=thumbnail.parentElement,preview=document.createElement('div');preview.className='camera-preview';thumbnailSource.style.gridTemplateColumns='1fr';form.insertBefore(preview,live);preview.append(live,thumbnail);
 const settings=document.createElement('details');settings.className='camera-settings';settings.innerHTML='<summary>Camera settings</summary>';form.insertBefore(settings,actions);for(const child of [...form.children])if(![head,preview,settings,actions].includes(child))settings.appendChild(child);
 if(overlayMode){
  settings.open=true;const placement=document.createElement('div');placement.className='overlay-placement';placement.innerHTML=`<label>Show over<select name="hostCameraSlot">${Array.from({length:9},(_,i)=>`<option value="${i+1}">Camera ${i+1}</option>`).join('')}</select></label><label>Viewport shape<select name="viewportShape"><option value="0">Rectangle</option><option value="1">Square</option><option value="2">Rounded rectangle</option><option value="3">Circle</option><option value="4">Oval</option><option value="5">Custom SVG</option></select></label><div class="custom-viewport-upload" hidden><label>Custom SVG mask<input class="custom-viewport-file" type="file" accept=".svg,image/svg+xml"></label><div class="custom-viewport-file-state"><span class="custom-viewport-status"></span><button type="button" class="secondary remove-custom-viewport">Remove</button></div><p>Path-based SVG, maximum 256 KB. Canvas-sized background paths are ignored automatically.</p>${rangeField('Mask rotation (degrees)','customViewportRotationDegrees',-180,180)}</div><input name="customViewportSourceName" type="hidden"><input name="customViewportPathData" type="hidden"><input name="customViewportViewBoxX" type="hidden" data-number="true"><input name="customViewportViewBoxY" type="hidden" data-number="true"><input name="customViewportViewBoxWidth" type="hidden" data-number="true"><input name="customViewportViewBoxHeight" type="hidden" data-number="true"><span class="overlay-group-title">Viewport size and position</span>${rangeField('Viewport width (% of tile)','viewportWidthPercent',10,95)}${rangeField('Viewport height (% of tile)','viewportHeightPercent',10,95)}${rangeField('Viewport horizontal position','viewportHorizontalPositionPercent',0,100)}${rangeField('Viewport vertical position','viewportVerticalPositionPercent',0,100)}${rangeField('Viewport opacity (%)','viewportOpacityPercent',20,100)}<span class="overlay-group-title">Video framing inside viewport</span>${rangeField('Video zoom (%)','zoomPercent',100,300,5)}${rangeField('Video horizontal position','imageHorizontalPositionPercent',0,100)}${rangeField('Video vertical position','imageVerticalPositionPercent',0,100)}<button type="button" class="secondary reset-doorbell-framing">Reset size and framing</button><p class="overlay-help">The video always keeps its original aspect ratio and covers the viewport, so no black bars are introduced. Use <b>Wall preview</b> to size and place the viewport. Use <b>Source framing</b> to see the complete camera image, then drag the blue visible area over what you want to keep. Position values run from 0 (left/top) through 50 (center) to 100 (right/bottom). Beta overlays use software-decoded frames to blend the video and controls together. Opacity changes apply live without restarting the stream. This may increase CPU usage for Doorbell and Garage; the nine main cameras retain their existing renderer.</p>`;settings.insertBefore(placement,settings.children[1]||null);const overlayNames=['hostCameraSlot','viewportShape','customViewportSourceName','customViewportPathData','customViewportViewBoxX','customViewportViewBoxY','customViewportViewBoxWidth','customViewportViewBoxHeight','customViewportRotationDegrees','viewportWidthPercent','viewportHeightPercent','viewportHorizontalPositionPercent','viewportVerticalPositionPercent','viewportOpacityPercent','zoomPercent','imageHorizontalPositionPercent','imageVerticalPositionPercent'];for(const name of overlayNames)form.elements[name].value=overlay[name];bindRangeFields(form,placement);bindCustomViewportUpload(form,placement);placement.querySelector('.reset-doorbell-framing').onclick=()=>{for(const [name,newValue] of Object.entries({viewportWidthPercent:50,viewportHeightPercent:50,viewportHorizontalPositionPercent:0,viewportVerticalPositionPercent:100,zoomPercent:100,imageHorizontalPositionPercent:50,imageVerticalPositionPercent:50})){form.elements[name].value=newValue;const range=form.querySelector('input[type="range"][data-sync="'+name+'"]');if(range)range.value=newValue}form.dispatchEvent(new Event('input',{bubbles:true}))};form.querySelector('button[type="submit"]').textContent='Save overlay';
 }
 form.querySelector('.restart-camera').onclick=async()=>{await runControl(`/api/control/cameras/${camera.slot}/restart`,{},overlayMode?`Restart the ${overlayLabel} stream?`:`Restart camera ${camera.slot}?`);setTimeout(()=>refreshThumbnail(thumbnail),3000)};
 form.onsubmit=async e=>{e.preventDefault();const state=form.querySelector('.save-state');state.textContent='Saving...';const overlayFields=new Set(['hostCameraSlot','viewportShape','customViewportSourceName','customViewportPathData','customViewportViewBoxX','customViewportViewBoxY','customViewportViewBoxWidth','customViewportViewBoxHeight','customViewportRotationDegrees','viewportWidthPercent','viewportHeightPercent','viewportHorizontalPositionPercent','viewportVerticalPositionPercent','viewportOpacityPercent','zoomPercent','imageHorizontalPositionPercent','imageVerticalPositionPercent']),payload={...camera};for(const el of form.elements)if(el.name&&!overlayFields.has(el.name))payload[el.name]=value(form,el.name);try{if(overlayMode){if(value(form,'viewportShape')===5&&!validCustomPathData(value(form,'customViewportPathData')))throw new Error('Upload a valid custom SVG before saving this viewport shape.');const overlayPayload={camera:payload};for(const name of overlayFields)overlayPayload[name]=value(form,name);overlay=await api(overlayDefinition.endpoint,{method:'PUT',body:JSON.stringify(overlayPayload)});camera=overlay.camera;for(const name of overlayFields){form.elements[name].value=overlay[name];const range=form.querySelector(`input[type="range"][data-sync="${name}"]`);if(range)range.value=overlay[name]}form.elements.viewportShape.dispatchEvent(new Event('change',{bubbles:true}));form.dispatchEvent(new Event('input',{bubbles:true}))}else camera=await api(`/api/cameras/${camera.slot}`,{method:'PUT',body:JSON.stringify(payload)});state.textContent='Saved - applying live';setTimeout(()=>refreshThumbnail(thumbnail),3000);setTimeout(()=>state.textContent='',2500)}catch(err){state.textContent=err.message}};
 return form
}
async function load(){const[status,config]=await Promise.all([api('/api/status'),api('/api/config')]);q('#host').textContent=`${status.hostname} - ${status.lanAddresses.join(', ')} - v${status.version}`;grid.replaceChildren(...config.cameras.map(camera=>cameraCard(camera)));const doorbellDefinition={kind:'doorbell',label:'Doorbell',slot:10,defaultHostSlot:2,endpoint:'/api/doorbell'},garageDefinition={kind:'garage',label:'Garage',slot:11,defaultHostSlot:3,endpoint:'/api/garage'};const doorbellCard=cameraCard(config.doorbellOverlay.camera,config.doorbellOverlay,doorbellDefinition),garageCard=cameraCard(config.garageOverlay.camera,config.garageOverlay,garageDefinition);doorbellGrid.replaceChildren(doorbellCard,createOverlayPreview(doorbellCard,doorbellDefinition));garageGrid.replaceChildren(garageCard,createOverlayPreview(garageCard,garageDefinition));const display=q('#displayForm');for(const name of['startFullScreen','preferredMonitor','hideMouseCursor','mouseCursorHideSeconds','showCameraNames','showCameraStats','keepViewerAlwaysOnTop']){const el=display.elements[name],val=config[name];if(el.type==='checkbox')el.checked=val;else el.value=val}q('#clock').textContent=new Date(status.currentTime).toLocaleString();await Promise.all([updateTelemetry(),loadLogs(),checkUpdates()]);clearInterval(window.telemetryTimer);window.telemetryTimer=setInterval(updateTelemetry,2000)}
async function updateTelemetry(){try{const t=await api('/api/telemetry'),s=t.system,v=t.viewer;q('#stats').innerHTML=[['Viewer',t.viewerConnected?'Connected':'Offline'],['CPU',pct(s.cpuPercent)],['RAM',s.ramUsedGb==null?'Unavailable':`${s.ramUsedGb} / ${s.ramTotalGb} GB`],['GPU',pct(s.gpuPercent)],['Video decode',pct(s.gpuVideoDecodePercent)],['Network down',rate(s.networkReceiveMbps)],['Network up',rate(s.networkSendMbps)],['Viewer memory',v?`${v.viewerMemoryMb} MB`:'Unavailable']].map(([l,x])=>`<div class="stat"><b>${x}</b><span>${l}</span></div>`).join('');for(const card of document.querySelectorAll('.camera-card[data-slot]')){const slot=Number(card.dataset.slot),c=v?.cameras.find(x=>x.slot===slot),box=card.querySelector('.camera-live');if(!c){box.classList.add('offline');box.querySelector('.state').textContent=t.viewerConnected?'Telemetry unavailable':'Viewer offline';box.querySelector('.live-details').textContent=card.dataset.kind!=='camera'?`No live telemetry for ${card.dataset.label} overlay`:`No live telemetry for position ${slot}`;continue}if(c.streamStartedAt&&card.dataset.streamStartedAt!==c.streamStartedAt){card.dataset.streamStartedAt=c.streamStartedAt;setTimeout(()=>refreshThumbnail(card.querySelector('.feed-thumbnail')),2500)}const live=c.state==='Live';box.classList.toggle('offline',!live);box.querySelector('.state').textContent=c.state;const resolution=c.width&&c.height?`${c.width}x${c.height}`:'-';box.querySelector('.live-details').textContent=`${c.fps.toFixed(1)} fps - ${resolution} - ${c.codec||'-'} - ${c.bitrateKbps.toFixed(0)} kb/s - R:${c.reconnectCount}`+(c.lastError?` - ${c.lastError}`:'')}}catch{}}
function pct(v){return v==null?'Unavailable':`${v.toFixed(1)}%`}function rate(v){return v==null?'Unavailable':`${v.toFixed(2)} Mb/s`}
async function runControl(url,body={},confirmation){if(confirmation&&!confirm(confirmation))return;const state=q('#controlState');state.textContent='Sending command...';try{const result=await api(url,{method:'POST',body:JSON.stringify(body)});state.textContent=result.message||'Command completed.'}catch(err){state.textContent=err.message}}
for(const button of document.querySelectorAll('[data-action]'))button.onclick=()=>runControl(`/api/control/viewer/${button.dataset.action}`,{},button.dataset.confirm);
for(const button of document.querySelectorAll('[data-system]'))button.onclick=()=>runControl(`/api/control/system/${button.dataset.system}`,{confirmed:true},'Reboot the Windows host? This will interrupt every camera.');
q('#displayForm').onsubmit=async e=>{e.preventDefault();const form=e.currentTarget,state=q('#displayState'),payload={startFullScreen:form.elements.startFullScreen.checked,preferredMonitor:Number(form.elements.preferredMonitor.value),hideMouseCursor:form.elements.hideMouseCursor.checked,mouseCursorHideSeconds:Number(form.elements.mouseCursorHideSeconds.value),showCameraNames:form.elements.showCameraNames.checked,showCameraStats:form.elements.showCameraStats.checked,keepViewerAlwaysOnTop:form.elements.keepViewerAlwaysOnTop.checked};state.textContent='Saving...';try{await api('/api/display',{method:'PUT',body:JSON.stringify(payload)});state.textContent='Saved - applying live';setTimeout(()=>state.textContent='',2500)}catch(err){state.textContent=err.message}};
async function loadLogs(){try{const result=await api('/api/logs?lines=400');q('#logView').textContent=result.lines.join('\n');q('#logView').scrollTop=q('#logView').scrollHeight}catch(err){q('#logView').textContent=err.message}}
q('#refreshLogs').onclick=loadLogs;
async function checkUpdates(){const state=q('#updateState'),button=q('#installUpdate');state.textContent='Checking update channel...';button.hidden=true;try{const update=await api('/api/update');const installed=String(update.installedVersion).replace(/\.0$/,'');const latest=update.latestVersion?String(update.latestVersion).replace(/\.0$/,''):null;state.textContent=update.updateAvailable?`Installed ${installed} - ${latest} is available.`:`Installed ${installed} - ${update.message}`;button.hidden=!update.updateAvailable}catch(err){state.textContent=err.message}}
q('#checkUpdates').onclick=checkUpdates;
q('#installUpdate').onclick=async()=>{if(!confirm('Install the available SpotMonitor update? The camera wall will briefly stop and Windows will request approval on the host.'))return;const state=q('#updateState');state.textContent='Staging and verifying update...';try{const result=await api('/api/update/install',{method:'POST',body:JSON.stringify({confirmed:true})});state.textContent=result.message;q('#installUpdate').hidden=true}catch(err){state.textContent=err.message}};
q('#passwordForm').onsubmit=async e=>{e.preventDefault();const form=e.currentTarget,state=q('#passwordState');state.textContent='Changing password...';try{const result=await api('/api/auth/password',{method:'POST',body:JSON.stringify({currentPassword:form.elements.currentPassword.value,newPassword:form.elements.newPassword.value})});form.reset();showLogin();q('#password').value='';q('#loginError').textContent=result.message}catch(err){state.textContent=err.message}};
(async()=>{const session=await api('/api/session');csrfToken=session.csrfToken;if(session.authenticated){showApp();await load()}else showLogin()})().catch(showLogin);
q('#importConfig').onclick=async()=>{const file=q('#configFile').files[0],state=q('#configState'),button=q('#importConfig');if(!file){state.textContent='Select a configuration JSON file first.';return}if(file.size>2*1024*1024){state.textContent='Configuration files must be no larger than 2 MB.';return}try{const text=await file.text();JSON.parse(text.replace(/^\uFEFF/,''));if(!confirm('Replace all camera, overlay, and display settings with this file? A backup of the current configuration will be saved first.'))return;button.disabled=true;state.textContent='Importing...';const result=await api('/api/config/import',{method:'POST',body:text});state.textContent=result.message;await load();q('#configFile').value=''}catch(error){state.textContent=error.message}finally{button.disabled=false}};
