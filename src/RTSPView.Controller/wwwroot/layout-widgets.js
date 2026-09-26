const layoutWidgetsUi=(()=>{
  const observers=new Map();
  const ui=kind=>kind==='aircraft'?aircraftUi:weatherUi;
  function region(widget,layout,width,height){const tile=layout.tiles.find(t=>t.kind!=='weather'&&t.kind!=='aircraft'&&t.cameraSlot===widget.hostCameraSlot);if(widget.hostCameraSlot&&!tile)return null;const r=tile?wallProportions(layout).bounds(tile):{left:0,top:0,width:1,height:1};return {left:r.left*width,top:r.top*height,width:r.width*width,height:r.height*height};}
  function bounds(widget,layout,width,height){const r=region(widget,layout,width,height);if(!r)return null;const b=ui(widget.kind).overlayBounds(widget,r.width,r.height);return {...b,left:r.left+b.left,top:r.top+b.top};}
  function edit(widget,layout,cameras,onSave,onDelete){
    const extension=(controls,placement,dialog,paint)=>{
      dialog.querySelector('h2').textContent=(widget.kind==='aircraft'?'Aircraft':'Weather')+' widget';dialog.querySelector('form > p').textContent='This widget belongs only to this layout. Save or apply the layout to persist changes.';
      const label=document.createElement('label');label.textContent='Attach widget to';const select=document.createElement('select');select.setAttribute('aria-label','Attach widget to');select.add(new Option('Free position on this layout','0'));
      for(const tile of layout.tiles.filter(t=>!['weather','aircraft'].includes(t.kind)&&t.cameraSlot>0))select.add(new Option(cameras.find(c=>c.slot===tile.cameraSlot)?.name||'Stream '+tile.cameraSlot,String(tile.cameraSlot)));
      select.value=String(placement.hostCameraSlot);select.onchange=()=>{placement.hostCameraSlot=Number(select.value);paint();};label.append(select);controls.prepend(label);
      if(onDelete){const remove=document.createElement('button');remove.type='button';remove.className='secondary';remove.textContent='Remove widget from layout';remove.onclick=()=>{onDelete();dialog.close();};controls.append(remove);}
      dialog.querySelector('button[type=submit]').textContent='Use in layout draft';
    };
    const args=[widget[widget.kind],(_,placement)=>onSave({...placement,id:widget.id,kind:widget.kind}),widget,widget.hostCameraSlot||null];if(widget.kind==='weather')args.push([]);args.push(extension);ui(widget.kind).editor(...args);
  }
  function add(kind,layout,cameras,onChange,slot=0){if((layout.widgets||[]).length>=16){uiDialogs.toast('Keep up to 16 widgets per layout.','error');return;}const widget={id:Array.from(crypto.getRandomValues(new Uint8Array(16)),v=>v.toString(16).padStart(2,'0')).join(''),kind,hostCameraSlot:slot,enabled:true,widthPercent:40,x:50,y:50,margin:12,[kind]:kind==='aircraft'?{...ui(kind).defaults(),textSizes:aircraftUi.balancedTextSizes()}:ui(kind).defaults()};edit(widget,layout,cameras,value=>{(layout.widgets??=[]).push(value);onChange();});}
  function render(board,layout,cameras,onChange){
    for(const [host,observer] of observers)if(!host.isConnected){observer.disconnect();observers.delete(host);}
    for(const widget of layout.widgets||[]){if(!widget.enabled||!pluginsUi.enabled(widget.kind))continue;const host=document.createElement('div');host.className='layout-widget';host.style.cssText='position:absolute;z-index:20;touch-action:none;';host.dataset.widgetId=widget.id;const card=ui(widget.kind).preview(widget[widget.kind],!!onChange,true);host.append(card);board.append(host);
      const place=()=>{const b=bounds(widget,layout,board.clientWidth,board.clientHeight);if(!b){host.hidden=true;return;}host.hidden=false;Object.assign(host.style,{left:b.left+'px',top:b.top+'px',width:b.width+'px',height:b.height+'px'});};place();const observer=new ResizeObserver(()=>{if(!host.isConnected){observer.disconnect();return;}place();});observer.observe(board);observers.set(host,observer);
      if(!onChange){host.style.pointerEvents='none';continue;}
      host.tabIndex=0;host.setAttribute('role','button');host.setAttribute('aria-label','Edit '+widget.kind+' widget');host.style.cursor='move';card.style.pointerEvents='none';
      const open=()=>edit(widget,layout,cameras,value=>{layout.widgets=layout.widgets.map(w=>w.id===widget.id?value:w);onChange();},()=>{layout.widgets=layout.widgets.filter(w=>w.id!==widget.id);onChange();});
      host.onkeydown=e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();open();}};
      host.onpointerdown=e=>{if(e.button!==0)return;e.stopPropagation();e.preventDefault();host.setPointerCapture(e.pointerId);const start={x:e.clientX,y:e.clientY,wx:widget.x,wy:widget.y};let moved=false;const r=region(widget,layout,board.clientWidth,board.clientHeight),b=bounds(widget,layout,board.clientWidth,board.clientHeight);if(!r||!b)return;
        host.onpointermove=m=>{const dx=m.clientX-start.x,dy=m.clientY-start.y;if(!moved&&Math.hypot(dx,dy)<4)return;moved=true;const margin=Math.min(widget.margin,r.width/2,r.height/2);widget.x=Math.round(Math.max(0,Math.min(100,start.wx+dx/Math.max(1,r.width-b.width-2*margin)*100)));widget.y=Math.round(Math.max(0,Math.min(100,start.wy+dy/Math.max(1,r.height-b.height-2*margin)*100)));place();};
        const stop=()=>{host.onpointermove=null;host.onpointerup=null;host.onpointercancel=null;};host.onpointerup=()=>{stop();if(moved)onChange();else open();};host.onpointercancel=()=>{stop();widget.x=start.wx;widget.y=start.wy;place();};
      };
    }
  }
  return {add,edit,render,bounds};
})();
