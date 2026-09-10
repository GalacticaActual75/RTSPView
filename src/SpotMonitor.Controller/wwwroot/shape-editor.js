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
  function open(form) {
    const dialog=document.createElement('dialog');dialog.className='shape-editor';
    dialog.setAttribute('aria-labelledby','shape-editor-title');
    dialog.innerHTML=`<h2 id="shape-editor-title">Viewport shape</h2><p>Choose a shape, or draw one continuous outline over the camera preview. The outline closes when you release.</p><div class="shape-presets"></div><div class="shape-tools"><button type="button" class="secondary" data-action="draw">Draw outline</button><button type="button" class="secondary" data-action="undo" disabled>Undo</button><label>Smoothing <input type="range" min="0" max="100" value="40" aria-label="Shape smoothing"><output>40%</output></label></div><canvas aria-label="Draw viewport outline over camera preview"></canvas><p class="shape-message" role="status"></p><div class="shape-footer"><button type="button" class="secondary" data-action="cancel">Cancel</button><button type="button" data-action="apply" disabled>Use shape</button></div>`;
    document.body.appendChild(dialog);
    const canvas=dialog.querySelector('canvas'),ctx=canvas.getContext('2d'),image=new Image(),slider=dialog.querySelector('input'),message=dialog.querySelector('.shape-message'),apply=dialog.querySelector('[data-action=apply]'),undo=dialog.querySelector('[data-action=undo]');
    const num=(name,fallback)=>Number(form.elements[name]?.value)||fallback;
    let aspect=16/9*num('viewportWidthPercent',50)/num('viewportHeightPercent',50);
    if([1,3].includes(num('viewportShape',0))) aspect=1;
    let selected=null,raw=[],history=[],drawing=false,armed=false,path='';
    function resize(){canvas.width=1000;canvas.height=Math.max(100,Math.round(1000/aspect));canvas.style.aspectRatio=String(aspect)}
    resize();
    function pathForSelection(){
      if(!selected) return '';
      if(selected.path) return selected.path;
      if(selected.raw) return outline(selected.raw,Number(slider.value));
      if([3,4].includes(selected.id)) return 'M0 500 A500 500 0 1 0 1000 500 A500 500 0 1 0 0 500 Z';
      if(selected.id===2){const rx=220*Math.min(1,1/aspect),ry=220*Math.min(1,aspect);return `M${rx} 0 H${1000-rx} Q1000 0 1000 ${ry} V${1000-ry} Q1000 1000 ${1000-rx} 1000 H${rx} Q0 1000 0 ${1000-ry} V${ry} Q0 0 ${rx} 0 Z`}
      return 'M0 0 H1000 V1000 H0 Z';
    }
    function render(){
      path=pathForSelection();ctx.setTransform(1,0,0,1,0,0);ctx.fillStyle='#192530';ctx.fillRect(0,0,canvas.width,canvas.height);
      if(image.naturalWidth){
        const scale=Math.max(canvas.width/image.naturalWidth,canvas.height/image.naturalHeight)*num('zoomPercent',100)/100,w=canvas.width/scale,h=canvas.height/scale;
        ctx.drawImage(image,(image.naturalWidth-w)*Number(form.elements.imageHorizontalPositionPercent.value)/100,(image.naturalHeight-h)*Number(form.elements.imageVerticalPositionPercent.value)/100,w,h,0,0,canvas.width,canvas.height);
      }
      ctx.save();ctx.scale(canvas.width/1000,canvas.height/1000);
      if(path&&!armed){const shape=new Path2D();if(selected.original){const v=selected.original;shape.addPath(new Path2D(path),new DOMMatrix().translate(500,500).rotate(v.rotation).translate(-500,-500).scale(1000/v.width,1000/v.height).translate(-v.x,-v.y))}else shape.addPath(new Path2D(path));const mask=new Path2D('M0 0 H1000 V1000 H0 Z');mask.addPath(shape);ctx.fillStyle='#000a';ctx.fill(mask,'evenodd');ctx.strokeStyle='#57bdff';ctx.lineWidth=4;ctx.stroke(shape)}
      if(drawing&&raw.length){ctx.beginPath();raw.forEach((p,i)=>i?ctx.lineTo(p.x,p.y):ctx.moveTo(p.x,p.y));ctx.strokeStyle='#57bdff';ctx.lineWidth=4;ctx.stroke()}
      ctx.restore();apply.disabled=!selected||!path||drawing;undo.disabled=!history.length;slider.disabled=!selected?.raw;
    }
    function remember(){history.push({selected,aspect});if(history.length>20)history.shift()}
    for(const name of [...Object.keys(presets),...Object.keys(extra)]){
      const button=document.createElement('button');button.type='button';button.className='secondary';button.textContent=name;
      button.onclick=()=>{remember();armed=false;selected=name in presets?{id:presets[name],name}:{id:5,path:extra[name],name};aspect=['Square','Circle','Rounded square'].includes(name)?1:16/9*num('viewportWidthPercent',50)/num('viewportHeightPercent',50);resize();message.textContent=`${name} selected. Sizing and framing remain available after applying.`;render()};dialog.querySelector('.shape-presets').appendChild(button);
    }
    dialog.querySelector('[data-action=draw]').onclick=()=>{armed=true;message.textContent='Draw a closed outline with your mouse, pen, or finger. A new outline replaces the previous one.';canvas.focus();render()};
    canvas.tabIndex=0;
    const point=e=>{const r=canvas.getBoundingClientRect();return{x:Math.max(0,Math.min(1000,(e.clientX-r.left)/r.width*1000)),y:Math.max(0,Math.min(1000,(e.clientY-r.top)/r.height*1000))}};
    canvas.onpointerdown=e=>{if(!armed||e.button!==0)return;drawing=true;raw=[point(e)];canvas.setPointerCapture(e.pointerId);render()};
    canvas.onpointermove=e=>{if(!drawing)return;const p=point(e);if(Math.hypot(p.x-raw.at(-1).x,p.y-raw.at(-1).y)>3&&raw.length<2000){raw.push(p);render()}};
    canvas.onpointerup=e=>{if(!drawing)return;drawing=false;armed=false;canvas.releasePointerCapture(e.pointerId);const xs=raw.map(p=>p.x),ys=raw.map(p=>p.y),area=Math.abs(raw.reduce((a,p,i)=>{const n=raw[(i+1)%raw.length];return a+p.x*n.y-n.x*p.y},0))/2;
      if(raw.length<6||Math.max(...xs)-Math.min(...xs)<30||Math.max(...ys)-Math.min(...ys)<30||area<900){message.textContent='Draw a larger outline with some enclosed area.'}else{remember();selected={id:5,raw:[...raw],name:'Drawn shape'};message.textContent='Adjust smoothing to soften mouse wobble. Use shape keeps this outline; Save overlay applies it to the wall.'}render()};
    canvas.onpointercancel=()=>{drawing=false;raw=[];render()};
    undo.onclick=()=>{const previous=history.pop();if(previous){selected=previous.selected;aspect=previous.aspect;resize();render()}};
    slider.oninput=()=>{dialog.querySelector('output').textContent=slider.value+'%';render()};
    const close=()=>dialog.close();dialog.querySelector('[data-action=cancel]').onclick=close;
    dialog.addEventListener('close',()=>{image.onload=null;image.onerror=null;dialog.remove();form.querySelector('.open-shape-editor')?.focus()},{once:true});
    apply.onclick=()=>{
      if(!selected||!path)return;
      if(selected.original){close();return}
      const set=(name,value)=>{form.elements[name].value=value;const range=form.querySelector(`[data-sync="${name}"]`);if(range)range.value=value};
      set('viewportShape',selected.id);
      if(selected.id===5){set('customViewportPathData',path);set('customViewportSourceName',selected.name);set('customViewportViewBoxX',0);set('customViewportViewBoxY',0);set('customViewportViewBoxWidth',1000);set('customViewportViewBoxHeight',1000);set('customViewportRotationDegrees',0)}
      if(selected.id===5&&aspect===1){const height=Math.max(18,Math.min(95,num('viewportHeightPercent',50)));set('viewportHeightPercent',height);set('viewportWidthPercent',Math.round(height*9/16))}
      form.elements.viewportShape.dispatchEvent(new Event('change',{bubbles:true}));form.dispatchEvent(new Event('input',{bubbles:true}));close();
    };
    const current=Number(form.elements.viewportShape.value);
    if(current!==5)selected={id:current,name:Object.keys(presets).find(name=>presets[name]===current)};
    else if(form.elements.customViewportPathData.value)selected={id:5,name:form.elements.customViewportSourceName.value,path:form.elements.customViewportPathData.value,original:{x:Number(form.elements.customViewportViewBoxX.value),y:Number(form.elements.customViewportViewBoxY.value),width:num('customViewportViewBoxWidth',1),height:num('customViewportViewBoxHeight',1),rotation:Number(form.elements.customViewportRotationDegrees.value)}};
    image.onload=render;image.onerror=()=>{message.textContent='Camera snapshot unavailable. You can still choose or draw a shape.'};image.src=form.querySelector('.feed-thumbnail').src;
    dialog.showModal();message.textContent='The preview uses your current viewport size and video framing. Choose a preset or Draw outline.';render();
  }
  return {open,outline};
})();
