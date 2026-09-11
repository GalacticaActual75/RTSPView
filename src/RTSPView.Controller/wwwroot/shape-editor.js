/* Drawn masks use the same path-only representation as uploaded SVG masks. */
const viewportShapeEditor = (() => {
  const presets = {Rectangle:0, Square:1, 'Rounded rectangle':2, Circle:3, Oval:4};
  const extra = {
    'Rounded square':'M220 0 H780 Q1000 0 1000 220 V780 Q1000 1000 780 1000 H220 Q0 1000 0 780 V220 Q0 0 220 0 Z',
    Triangle:'M500 0 L1000 1000 L0 1000 Z',
    Diamond:'M500 0 L1000 500 L500 1000 L0 500 Z',
    Hexagon:'M250 0 L750 0 L1000 500 L750 1000 L250 1000 L0 500 Z'
  };
  // Resample by distance before smoothing so mouse speed does not change the result.
  function outline(raw, amount) {
    if(raw.length < 3) return '';
    let points = [], distance = 0;
    const ring = [...raw, raw[0]];
    const perimeter=ring.slice(1).reduce((sum,p,i)=>sum+Math.hypot(p.x-ring[i].x,p.y-ring[i].y),0),spacing=Math.max(8,perimeter/600);
    for(let i=1;i<ring.length;i++) {
      const a=ring[i-1], b=ring[i], length=Math.hypot(b.x-a.x,b.y-a.y);
      if(!length) continue;
      for(let step=distance;step<length;step+=spacing) points.push({x:a.x+(b.x-a.x)*step/length,y:a.y+(b.y-a.y)*step/length});
      distance=(distance-length)%spacing; if(distance<0) distance+=spacing;
    }
    if(points.length<3) return '';
    const radius=Math.min(Math.round(amount/10),Math.floor((points.length-1)/2));
    if(radius) points=points.map((_,i)=>{
      let x=0,y=0,weight=0;
      for(let j=-radius;j<=radius;j++){const p=points[(i+j+points.length)%points.length],w=radius+1-Math.abs(j);x+=p.x*w;y+=p.y*w;weight+=w}
      return {x:x/weight,y:y/weight};
    });
    const pair=p=>`${p.x.toFixed(2)} ${p.y.toFixed(2)}`;
    if(!radius) return `M${points.map(pair).join(' L')} Z`;
    const midpoint=(a,b)=>({x:(a.x+b.x)/2,y:(a.y+b.y)/2});
    return `M${pair(midpoint(points.at(-1),points[0]))} `+points.map((p,i)=>`Q${pair(p)} ${pair(midpoint(p,points[(i+1)%points.length]))}`).join(' ')+' Z';
  }
  function placement(raw) {
    const xs=raw.map(p=>p.x),ys=raw.map(p=>p.y),minX=Math.min(...xs),maxX=Math.max(...xs),minY=Math.min(...ys),maxY=Math.max(...ys);
    if(maxX-minX>950||maxY-minY>950) return null;
    const width=Math.min(95,Math.max(10,Math.ceil((maxX-minX)/10)+1)),height=Math.min(95,Math.max(10,Math.ceil((maxY-minY)/10)+1));
    const left=Math.max(0,Math.min(1000-width*10,(minX+maxX-width*10)/2)),top=Math.max(0,Math.min(1000-height*10,(minY+maxY-height*10)/2));
    const horizontal=Math.round(left/(1000-width*10)*100),vertical=Math.round(top/(1000-height*10)*100);
    return {width,height,horizontal,vertical,x:(1000-width*10)*horizontal/100,y:(1000-height*10)*vertical/100};
  }
  function pointOutline(points, smoothing) {
    if(points.length<3)return '';
    return Number(smoothing)===0?'M'+points.map(p=>`${p.x.toFixed(2)} ${p.y.toFixed(2)}`).join(' L')+' Z':outline(points,Number(smoothing));
  }
  function open(form) {
    const dialog=document.createElement('dialog');dialog.className='shape-editor';
    dialog.setAttribute('aria-labelledby','shape-editor-title');
    dialog.innerHTML=`<h2 id="shape-editor-title">Viewport shape</h2><p>Choose a shape, or draw where the overlay belongs on the background camera. Use Free draw to drag an outline, or Point outline to click its corners.</p><div class="shape-presets"></div><div class="shape-tools"><button type="button" class="secondary" data-action="draw">Draw outline</button><button type="button" class="secondary" data-action="undo" disabled>Undo</button><label>Smoothing <input type="range" min="0" max="100" value="40" aria-label="Shape smoothing"><output>40%</output></label></div><canvas aria-label="Draw viewport outline over background camera"></canvas><p class="shape-message" role="status"></p><div class="shape-footer"><button type="button" class="secondary" data-action="cancel">Cancel</button><button type="button" data-action="apply" disabled>Use shape</button></div>`;
    document.body.appendChild(dialog);
    const canvas=dialog.querySelector('canvas'),ctx=canvas.getContext('2d'),image=new Image(),overlayImage=new Image(),slider=dialog.querySelector('input'),message=dialog.querySelector('.shape-message'),apply=dialog.querySelector('[data-action=apply]'),undo=dialog.querySelector('[data-action=undo]');
    const num=(name,fallback)=>{const field=form.elements[name];return field&&field.value!==''&&Number.isFinite(Number(field.value))?Number(field.value):fallback};
    let aspect=16/9*num('viewportWidthPercent',50)/num('viewportHeightPercent',50);
    if([1,3].includes(num('viewportShape',0))) aspect=1;
    let selected=null,raw=[],history=[],drawing=false,armed=false,path='',pointMode=false,hover=null;
    const drawButton=dialog.querySelector('[data-action=draw]');drawButton.textContent='Free draw';
    const pointButton=document.createElement('button');pointButton.type='button';pointButton.className='secondary';pointButton.textContent='Point outline';drawButton.after(pointButton);
    const finishButton=document.createElement('button');finishButton.type='button';finishButton.className='secondary';finishButton.textContent='Close outline';finishButton.hidden=true;pointButton.after(finishButton);
    function resize(){canvas.width=1600;canvas.height=900;canvas.style.aspectRatio='16 / 9'}
    resize();
    function viewport(){
      if(selected?.placement){const p=selected.placement;return{x:p.x,y:p.y,width:p.width*10,height:p.height*10}}
      let width=num('viewportWidthPercent',50)*10,height=num('viewportHeightPercent',50)*10;
      if([1,3].includes(selected?.id)||selected?.name==='Rounded square'){const side=Math.min(width*1.6,height*.9);width=side/1.6;height=side/.9}
      return{x:(1000-width)*num('viewportHorizontalPositionPercent',0)/100,y:(1000-height)*num('viewportVerticalPositionPercent',100)/100,width,height};
    }
    function pathForSelection(){
      if(!selected) return '';
      if(selected.path) return selected.path;
      if(selected.raw) return selected.pointMode?pointOutline(selected.raw,slider.value):outline(selected.raw,Number(slider.value));
      if([3,4].includes(selected.id)) return 'M0 500 A500 500 0 1 0 1000 500 A500 500 0 1 0 0 500 Z';
      if(selected.id===2){const rx=220*Math.min(1,1/aspect),ry=220*Math.min(1,aspect);return `M${rx} 0 H${1000-rx} Q1000 0 1000 ${ry} V${1000-ry} Q1000 1000 ${1000-rx} 1000 H${rx} Q0 1000 0 ${1000-ry} V${ry} Q0 0 ${rx} 0 Z`}
      return 'M0 0 H1000 V1000 H0 Z';
    }
    function render(){
      path=pathForSelection();ctx.setTransform(1,0,0,1,0,0);ctx.fillStyle='#192530';ctx.fillRect(0,0,canvas.width,canvas.height);
      if(image.naturalWidth){
        const scale=Math.max(canvas.width/image.naturalWidth,canvas.height/image.naturalHeight),w=canvas.width/scale,h=canvas.height/scale;
        ctx.drawImage(image,(image.naturalWidth-w)/2,(image.naturalHeight-h)/2,w,h,0,0,canvas.width,canvas.height);
      }
      ctx.save();ctx.scale(canvas.width/1000,canvas.height/1000);
      if(path&&!armed){
        const box=viewport(),shape=new Path2D();
        if(selected.raw)shape.addPath(new Path2D(path));
        else {let transform=new DOMMatrix().translate(box.x,box.y).scale(box.width/1000,box.height/1000);if(selected.original){const v=selected.original;transform=transform.translate(500,500).scale(1/(box.width*1.6),1/(box.height*.9)).rotate(v.rotation).scale(box.width*1.6,box.height*.9).translate(-500,-500).scale(1000/v.width,1000/v.height).translate(-v.x,-v.y)}shape.addPath(new Path2D(path),transform)}
        if(overlayImage.naturalWidth){ctx.save();ctx.clip(shape,'evenodd');ctx.globalAlpha=num('viewportOpacityPercent',100)/100;const scale=Math.max(box.width*1.6/overlayImage.naturalWidth,box.height*.9/overlayImage.naturalHeight)*num('zoomPercent',100)/100,w=box.width*1.6/scale,h=box.height*.9/scale;ctx.drawImage(overlayImage,(overlayImage.naturalWidth-w)*num('imageHorizontalPositionPercent',50)/100,(overlayImage.naturalHeight-h)*num('imageVerticalPositionPercent',50)/100,w,h,box.x,box.y,box.width,box.height);ctx.restore()}
        ctx.strokeStyle='#57bdff';ctx.lineWidth=3;ctx.stroke(shape);
      }
      if((drawing||armed&&pointMode)&&raw.length){ctx.beginPath();raw.forEach((p,i)=>i?ctx.lineTo(p.x,p.y):ctx.moveTo(p.x,p.y));ctx.strokeStyle='#57bdff';ctx.lineWidth=4;ctx.stroke();if(pointMode){if(hover){ctx.setLineDash([8,6]);ctx.lineTo(hover.x,hover.y);ctx.stroke();ctx.setLineDash([])}raw.forEach((p,i)=>{ctx.beginPath();ctx.ellipse(p.x,p.y,5,9,0,0,Math.PI*2);ctx.fillStyle=i===0?'#38d6a4':'#57bdff';ctx.fill()})}}
      ctx.restore();apply.disabled=!selected||!path||drawing||armed;undo.disabled=armed?raw.length===0:!history.length;slider.disabled=armed||!selected?.raw;
      finishButton.hidden=!(armed&&pointMode);finishButton.disabled=raw.length<3;
      drawButton.setAttribute('aria-pressed',String(armed&&!pointMode));pointButton.setAttribute('aria-pressed',String(armed&&pointMode));
    }
    function remember(){history.push({selected,aspect,smoothing:slider.value});if(history.length>20)history.shift()}
    for(const name of [...Object.keys(presets),...Object.keys(extra)]){
      const button=document.createElement('button');button.type='button';button.className='secondary';button.textContent=name;
      button.onclick=()=>{remember();armed=false;drawing=false;raw=[];hover=null;selected=name in presets?{id:presets[name],name}:{id:5,path:extra[name],name};aspect=['Square','Circle','Rounded square'].includes(name)?1:16/9*num('viewportWidthPercent',50)/num('viewportHeightPercent',50);resize();message.textContent=`${name} selected. Sizing and framing remain available after applying.`;render()};dialog.querySelector('.shape-presets').appendChild(button);
    }
    drawButton.onclick=()=>start(false);
    pointButton.onclick=()=>start(true);
    function start(points){armed=true;pointMode=points;drawing=false;raw=[];hover=null;message.textContent=points?'Click to add corners. Click the green first point, Close outline, or press Enter to finish. Undo / Backspace removes the last point; Escape cancels this outline.':'Draw a closed outline with your mouse, pen, or finger. Release to finish.';canvas.focus();render()}
    canvas.tabIndex=0;
    const point=e=>{const r=canvas.getBoundingClientRect();return{x:Math.max(0,Math.min(1000,(e.clientX-r.left)/r.width*1000)),y:Math.max(0,Math.min(1000,(e.clientY-r.top)/r.height*1000))}};
    canvas.onpointerdown=e=>{if(!armed||e.button!==0)return;canvas.focus();const p=point(e);if(pointMode){const rect=canvas.getBoundingClientRect();if(raw.length>=3&&Math.hypot((p.x-raw[0].x)*rect.width/1000,(p.y-raw[0].y)*rect.height/1000)<=12){finish();return}if(raw.length<256&&(!raw.length||Math.hypot(p.x-raw.at(-1).x,p.y-raw.at(-1).y)>3))raw.push(p);hover=null;message.textContent=`${raw.length} points. Add corners or close the outline (at least 3 points).`;render();return}drawing=true;raw=[p];canvas.setPointerCapture(e.pointerId);render()};
    canvas.onpointermove=e=>{if(armed&&pointMode){hover=point(e);render();return}if(!drawing)return;const p=point(e);if(Math.hypot(p.x-raw.at(-1).x,p.y-raw.at(-1).y)>3&&raw.length<2000){raw.push(p);render()}};
    canvas.onpointerleave=()=>{hover=null;if(pointMode)render()};
    canvas.onpointerup=e=>{if(!drawing)return;drawing=false;canvas.releasePointerCapture(e.pointerId);finish()};
    function finish(){
      const xs=raw.map(p=>p.x),ys=raw.map(p=>p.y),area=Math.abs(raw.reduce((a,p,i)=>{const n=raw[(i+1)%raw.length];return a+p.x*n.y-n.x*p.y},0))/2;
      if(raw.length<(pointMode?3:6)||Math.max(...xs)-Math.min(...xs)<30||Math.max(...ys)-Math.min(...ys)<30||area<900){message.textContent='Make a larger outline with some enclosed area.'}else if(!placement(raw)){message.textContent='Keep the outline within 95% of the background width and height, matching the viewport size limits.'}else{remember();selected={id:5,raw:[...raw],pointMode,placement:placement(raw),name:pointMode?'Point outline':'Drawn shape'};slider.value=pointMode?'0':'40';dialog.querySelector('output').textContent=slider.value+'%';armed=false;hover=null;message.textContent='The overlay fills your outline. Adjust smoothing, then Use shape to keep its size and position.'}if(!pointMode)armed=false;render();
    }
    finishButton.onclick=finish;
    canvas.onkeydown=e=>{if(!armed)return;if(e.key==='Enter'&&pointMode){e.preventDefault();finish()}else if(['Backspace','Delete'].includes(e.key)&&pointMode){e.preventDefault();raw.pop();render()}};
    dialog.addEventListener('cancel',e=>{if(armed){e.preventDefault();armed=false;drawing=false;raw=[];hover=null;message.textContent='Outline canceled. Your previous shape is unchanged.';render()}});
    canvas.onpointercancel=()=>{drawing=false;raw=[];render()};
    undo.onclick=()=>{if(armed){raw.pop();render();return}const previous=history.pop();if(previous){selected=previous.selected;aspect=previous.aspect;slider.value=previous.smoothing;dialog.querySelector('output').textContent=slider.value+'%';resize();render()}};
    slider.oninput=()=>{dialog.querySelector('output').textContent=slider.value+'%';render()};
    const close=()=>dialog.close();dialog.querySelector('[data-action=cancel]').onclick=close;
    dialog.addEventListener('close',()=>{image.onload=null;image.onerror=null;overlayImage.onload=null;overlayImage.onerror=null;dialog.remove();form.querySelector('.open-shape-editor')?.focus()},{once:true});
    apply.onclick=()=>{
      if(!selected||!path)return;
      if(selected.original){close();return}
      const set=(name,value)=>{form.elements[name].value=value;const range=form.querySelector(`[data-sync="${name}"]`);if(range)range.value=value};
      set('viewportShape',selected.id);
      if(selected.id===5){set('customViewportPathData',path);set('customViewportSourceName',selected.name);set('customViewportViewBoxX',0);set('customViewportViewBoxY',0);set('customViewportViewBoxWidth',1000);set('customViewportViewBoxHeight',1000);set('customViewportRotationDegrees',0)}
      if(selected.placement){const p=selected.placement;set('viewportWidthPercent',p.width);set('viewportHeightPercent',p.height);set('viewportHorizontalPositionPercent',p.horizontal);set('viewportVerticalPositionPercent',p.vertical);set('customViewportViewBoxX',p.x);set('customViewportViewBoxY',p.y);set('customViewportViewBoxWidth',p.width*10);set('customViewportViewBoxHeight',p.height*10)}
      else if(selected.id===5&&aspect===1){const box=viewport();set('viewportHeightPercent',Math.max(10,Math.round(box.height/10)));set('viewportWidthPercent',Math.max(10,Math.round(box.width/10)))}
      form.elements.viewportShape.dispatchEvent(new Event('change',{bubbles:true}));form.dispatchEvent(new Event('input',{bubbles:true}));close();
    };
    const current=Number(form.elements.viewportShape.value);
    if(current!==5)selected={id:current,name:Object.keys(presets).find(name=>presets[name]===current)};
    else if(form.elements.customViewportPathData.value)selected={id:5,name:form.elements.customViewportSourceName.value,path:form.elements.customViewportPathData.value,original:{x:Number(form.elements.customViewportViewBoxX.value),y:Number(form.elements.customViewportViewBoxY.value),width:num('customViewportViewBoxWidth',1),height:num('customViewportViewBoxHeight',1),rotation:Number(form.elements.customViewportRotationDegrees.value)}};
    image.onload=render;image.onerror=()=>{message.textContent='Background camera snapshot unavailable. Check the camera selected in Show over.'};image.src='/api/cameras/'+num('hostCameraSlot',2)+'/thumbnail?v='+Date.now();overlayImage.onload=render;overlayImage.src=form.querySelector('.feed-thumbnail').src;
    dialog.showModal();message.textContent='Draw where the overlay should appear on background Camera '+num('hostCameraSlot',2)+'. The outline sets its size and position.';render();
  }
  return {open,outline,placement,pointOutline};
})();
