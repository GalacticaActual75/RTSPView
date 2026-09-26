const layoutWidgetsUi=(()=>{
  const observers=new Map();
  const ui=kind=>kind==='aircraft'?aircraftUi:weatherUi;
  function region(widget,layout,width,height){return {left:0,top:0,width,height};}
  function bounds(widget,layout,width,height){const margin=Math.min(widget.margin,width/2,height/2),aw=Math.max(0,width-2*margin),ah=Math.max(0,height-2*margin),w=aw*widget.widthPercent/100;const natural=ui(widget.kind).overlayBounds({...widget,widthPercent:100,margin:0,x:0,y:0},w,ah).height;const h=widget.heightPercent==null?natural:ah*widget.heightPercent/100;return {width:w,height:h,left:margin+(aw-w)*widget.x/100,top:margin+(ah-h)*widget.y/100};}
  function edit(widget,layout,cameras,onSave,onDelete){
    const extension=(controls,placement,dialog,paint)=>{
      dialog.querySelector('h2').textContent=(widget.kind==='aircraft'?'Aircraft':'Weather')+' widget';dialog.querySelector('form > p').textContent='This widget belongs only to this layout. Save or apply the layout to persist changes.';
      placement.hostCameraSlot=0;
      for(const width of controls.querySelectorAll('input[aria-label="Maximum width (%) value"],input[aria-label="Maximum width (%) slider"]')){width.min=1;width.max=100;width.value=placement.widthPercent;const output=width.parentElement.querySelector("output");if(output)output.textContent=placement.widthPercent;}
      const size=document.createElement('label');size.textContent='Height (% of layout, blank for automatic)';const height=document.createElement('input');height.type='number';height.min=.01;height.max=100;height.step="any";height.value=placement.heightPercent??'';height.setAttribute('aria-label','Widget height (%)');height.oninput=()=>{if(height.value===''||height.checkValidity()){placement.heightPercent=height.value===''?null:Number(height.value);paint();}};size.append(height);controls.append(size);
      if(onDelete){const remove=document.createElement('button');remove.type='button';remove.className='secondary';remove.textContent='Remove widget from layout';remove.onclick=()=>{onDelete();dialog.close();};controls.append(remove);}
      dialog.querySelector('button[type=submit]').textContent='Use in layout draft';
    };
    const args=[widget[widget.kind],(_,placement)=>onSave({...placement,id:widget.id,kind:widget.kind}),widget,null];if(widget.kind==='weather')args.push([]);args.push(extension);ui(widget.kind).editor(...args);
  }
  function add(kind,layout,cameras,onChange){if((layout.widgets||[]).length>=64){uiDialogs.toast('Keep up to 64 widgets per layout.','error');return;}const widget={id:Array.from(crypto.getRandomValues(new Uint8Array(16)),v=>v.toString(16).padStart(2,'0')).join(''),kind,hostCameraSlot:0,enabled:true,widthPercent:40,x:50,y:50,margin:12,[kind]:kind==='aircraft'?{...ui(kind).defaults(),textSizes:aircraftUi.balancedTextSizes()}:ui(kind).defaults()};edit(widget,layout,cameras,value=>{(layout.widgets??=[]).push(value);onChange();});}
  function render(board,layout,cameras,onChange,selection={}){
    for(const [host,observer] of observers)if(!host.isConnected){observer.disconnect();observers.delete(host);}
    for(const widget of layout.widgets||[]){if((!widget.enabled&&!onChange)||!pluginsUi.enabled(widget.kind))continue;const host=document.createElement('div');host.className='layout-widget';host.style.cssText='position:absolute;z-index:20;touch-action:none;';host.dataset.widgetId=widget.id;host.classList.toggle('selected',selection.selectedId===widget.id);host.setAttribute('aria-pressed',String(selection.selectedId===widget.id));const card=ui(widget.kind).preview(widget[widget.kind],!!onChange,true);host.append(card);board.append(host);
      const logicalWidth=layout.outputWidth||(layout.aspectRatio==='9:16'?1080:1920),logicalHeight=layout.outputHeight||(layout.aspectRatio==='9:16'?1920:1080);
      const place=()=>{const scale=board.clientWidth/logicalWidth,b=bounds(widget,layout,logicalWidth,logicalHeight);if(!b){host.hidden=true;return;}host.hidden=false;Object.assign(host.style,{left:b.left*scale+'px',top:b.top*scale+'px',width:b.width*scale+'px',height:b.height*scale+'px'});Object.assign(card.style,{width:b.width+'px',height:b.height+'px',transformOrigin:'top left',transform:'scale('+scale+')'});};place();const observer=new ResizeObserver(()=>{if(!host.isConnected){observer.disconnect();return;}place();});observer.observe(board);observers.set(host,observer);
      if(!onChange){host.style.pointerEvents='none';continue;}
      host.tabIndex=0;host.setAttribute('role','button');host.setAttribute('aria-label','Edit '+widget.kind+' widget');host.style.cursor='move';card.style.pointerEvents='none';
      const open=()=>{selection.onSelect?.(widget.id);edit(widget,layout,cameras,value=>{layout.widgets=layout.widgets.map(w=>w.id===widget.id?value:w);onChange();},()=>{layout.widgets=layout.widgets.filter(w=>w.id!==widget.id);onChange();});};
      host.onkeydown=e=>{if(e.key==='Delete'){e.preventDefault();selection.onRemove?.(widget.id);return;}if(e.key==='Enter'||e.key===' '){e.preventDefault();open();}};
      host.onpointerdown=e=>{if(e.button!==0)return;e.stopPropagation();e.preventDefault();host.setPointerCapture(e.pointerId);const start={x:e.clientX,y:e.clientY,wx:widget.x,wy:widget.y};let moved=false;const r=region(widget,layout,board.clientWidth,board.clientHeight),b={width:host.clientWidth,height:host.clientHeight};if(!r)return;
        host.onpointermove=m=>{const dx=m.clientX-start.x,dy=m.clientY-start.y;if(!moved&&Math.hypot(dx,dy)<4)return;moved=true;const margin=Math.min(widget.margin*board.clientWidth/logicalWidth,r.width/2,r.height/2);widget.x=Math.max(0,Math.min(100,start.wx+dx/Math.max(1,r.width-b.width-2*margin)*100));widget.y=Math.max(0,Math.min(100,start.wy+dy/Math.max(1,r.height-b.height-2*margin)*100));place();};
        const stop=()=>{host.onpointermove=null;host.onpointerup=null;host.onpointercancel=null;};host.onpointerup=()=>{stop();if(moved){selection.onSelect?.(widget.id,false);onChange();}else open();};host.onpointercancel=()=>{stop();widget.x=start.wx;widget.y=start.wy;place();};
      };
    }
  }
  return {add,edit,render,bounds};
})();
