const layoutWidgetsUi=(()=>{
  const observers=new Map();
  const ui=kind=>kind==='aircraft'?aircraftUi:weatherUi;
  function region(widget,layout,width,height){return {left:0,top:0,width,height};}
  function grid(layout){return typeof wallProportions==='function'?wallProportions(layout):{rows:Array(layout.rows||3).fill(1/(layout.rows||3)),columns:Array(layout.columns||3).fill(1/(layout.columns||3))};}
  function bounds(widget,layout,width,height){
    if(widget.cell){const g=grid(layout),c=widget.cell,row=Math.min(c.row,g.rows.length-1),column=Math.min(c.column,g.columns.length-1),sum=a=>a.reduce((n,v)=>n+v,0);return {left:sum(g.columns.slice(0,column))*width,top:sum(g.rows.slice(0,row))*height,width:sum(g.columns.slice(column,column+c.columnSpan))*width,height:sum(g.rows.slice(row,row+c.rowSpan))*height};}
const margin=Math.min(widget.margin,width/2,height/2),aw=Math.max(0,width-2*margin),ah=Math.max(0,height-2*margin),w=aw*widget.widthPercent/100;const natural=ui(widget.kind).overlayBounds({...widget,widthPercent:100,margin:0,x:0,y:0},w,ah).height;const h=widget.heightPercent==null?natural:ah*widget.heightPercent/100;return {width:w,height:h,left:margin+(aw-w)*widget.x/100,top:margin+(ah-h)*widget.y/100};}
  function contentScale(widget,b){
    return 1;
  }

  function resized(widget,b,width,height,dx,dy,edge){
    const margin=Math.min(widget.margin,width/2,height/2),aw=width-2*margin,ah=height-2*margin,west=edge.includes('w'),north=edge.includes('n');
    const maxWidth=west?b.left+b.width-margin:width-margin-b.left,maxHeight=north?b.top+b.height-margin:height-margin-b.top;
    const horizontal=edge.includes('e')||west,vertical=edge.includes('s')||north;
    const w=horizontal?Math.max(Math.min(maxWidth,Math.max(160,aw*.01)),Math.min(maxWidth,b.width+(west?-dx:dx))):b.width;
    const h=vertical?Math.max(Math.min(maxHeight,96),Math.min(maxHeight,b.height+(north?-dy:dy))):b.height;
    const left=west?b.left+b.width-w:b.left,top=north?b.top+b.height-h:b.top;
    const percent=value=>Math.max(0,Math.min(100,value));return {widthPercent:Math.max(1,percent(w/aw*100)),heightPercent:percent(h/ah*100),x:aw>w?percent((left-margin)/(aw-w)*100):0,y:ah>h?percent((top-margin)/(ah-h)*100):0,contentScale:1};
  }

  const output=layout=>({width:layout.outputWidth||(layout.aspectRatio==='9:16'?1080:1920),height:layout.outputHeight||(layout.aspectRatio==='9:16'?1920:1080)});
  function setRectangle(widget,layout,rectangle){
    const size=output(layout),margin=Math.min(widget.margin,size.width/2,size.height/2),aw=size.width-2*margin,ah=size.height-2*margin;
    const width=Math.max(Math.min(160,aw),Math.min(aw,rectangle.width)),height=Math.max(Math.min(96,ah),Math.min(ah,rectangle.height));
    const left=Math.max(margin,Math.min(size.width-margin-width,rectangle.left)),top=Math.max(margin,Math.min(size.height-margin-height,rectangle.top));
    Object.assign(widget,{widthPercent:width/aw*100,heightPercent:height/ah*100,x:aw>width?(left-margin)/(aw-width)*100:0,y:ah>height?(top-margin)/(ah-height)*100:0,contentScale:1});
  }
  function inspector(parent,widget,layout,onChange){
    const mode=document.createElement('label'),select=document.createElement('select');mode.textContent='Placement';select.setAttribute('aria-label','Widget placement');select.add(new Option('Grid tile','grid'));select.add(new Option('Floating over wall','free'));select.value=widget.cell?'grid':'free';mode.append(select);parent.append(mode);
    select.onchange=()=>{const size=output(layout),b=bounds(widget,layout,size.width,size.height);if(select.value==='grid')widget.cell={row:0,column:0,rowSpan:1,columnSpan:1};else{delete widget.cell;setRectangle(widget,layout,b);}onChange();};
    if(widget.cell){
      const c=widget.cell;for(const [key,label,max] of [['row','Row',layout.rows],['column','Column',layout.columns],['rowSpan','Row span',layout.rows],['columnSpan','Column span',layout.columns]]){
        const wrap=document.createElement('label'),input=document.createElement('input'),position=key==='row'||key==='column';wrap.textContent=label;input.type='number';input.min=1;input.max=max;input.value=position?Math.min(c[key]+1,max):Math.min(c[key],max);input.setAttribute('aria-label','Widget '+label);input.onchange=()=>{if(input.value&&input.checkValidity()){c[key]=Number(input.value)-(position?1:0);onChange();}};wrap.append(input);parent.append(wrap);
      }
      const note=document.createElement('p');note.className='designer-help';note.textContent='Follows grid cells and spans. Widgets remain above any camera in the same cells; camera assignments are preserved.';parent.append(note);return;
    }
    const advanced=document.createElement('details'),summary=document.createElement('summary');summary.textContent='Precise placement';advanced.append(summary);parent.append(advanced);
    const commonParent=parent;parent=advanced;
    const size=output(layout),b=bounds(widget,layout,size.width,size.height);
    const description=document.createElement('p');description.className='designer-help';description.textContent='Position and size in wall pixels. Width and height resize independently.';parent.append(description);
    const fields=document.createElement('div');fields.className='widget-dimensions';parent.append(fields);
    for(const [key,label] of [['left','Left (px)'],['top','Top (px)'],['width','Width (px)'],['height','Height (px)']]){
      const wrap=document.createElement('label'),input=document.createElement('input');wrap.textContent=label;input.type='number';input.step=1;input.min=key==='width'?160:key==='height'?96:0;input.max=key==='left'||key==='width'?size.width:size.height;input.value=Math.round(b[key]);input.setAttribute('aria-label','Widget '+label);
      input.onchange=()=>{if(input.value!==''&&input.checkValidity()){setRectangle(widget,layout,{...b,[key]:Number(input.value)});onChange();}};wrap.append(input);fields.append(wrap);
    }
    const presets=document.createElement('div');presets.className='widget-size-presets';commonParent.append(presets);
    for(const [label,w,h] of [['Compact',320,180],['Standard',420,240],['Large',560,320]]){const button=document.createElement('button');button.type='button';button.textContent=label;button.onclick=()=>{const factor=widget.kind==='aircraft'?2:1,width=w*factor,height=h*(widget.kind==='aircraft'?1.5:1);setRectangle(widget,layout,{left:b.left+(b.width-width)*widget.x/100,top:b.top+(b.height-height)*widget.y/100,width,height});onChange();};presets.append(button);}
    const fit=document.createElement('button');fit.type='button';fit.textContent='Fit height to content';fit.title='Keep this width and use the natural content height';fit.onclick=()=>{widget.heightPercent=null;widget.contentScale=1;onChange();};parent.append(fit);
    const align=document.createElement('div');align.className='widget-align';align.setAttribute('aria-label','Align widget within wall');parent.append(align);
    for(const [x,y,label,glyph] of [[0,0,'Top left','↖'],[50,0,'Top center','↑'],[100,0,'Top right','↗'],[0,50,'Middle left','←'],[50,50,'Center','◎'],[100,50,'Middle right','→'],[0,100,'Bottom left','↙'],[50,100,'Bottom center','↓'],[100,100,'Bottom right','↘']]){const button=document.createElement('button');button.type='button';button.textContent=glyph;button.title=label;button.setAttribute('aria-label','Align widget '+label.toLowerCase());button.onclick=()=>{widget.x=x;widget.y=y;onChange();};align.append(button);}
    if(b.width<(widget.kind==='aircraft'?480:240)||b.height<(widget.kind==='aircraft'?220:140)){const warning=document.createElement('p');warning.className='designer-help';warning.textContent='Small on the full wall. Use a size preset for more readable text and details.';parent.append(warning);}
  }
  function edit(widget,layout,cameras,onSave,onDelete){
    const extension=(controls,placement,dialog,paint)=>{
      dialog.querySelector('h2').textContent=(widget.kind==='aircraft'?'Aircraft':'Weather')+' widget';dialog.querySelector('form > p').textContent='This widget belongs only to this layout. Save or apply the layout to persist changes.';
      placement.hostCameraSlot=0;
      for(const group of controls.querySelectorAll('details'))if(group.querySelector('summary')?.textContent==='Position and size')group.remove();
      const positionNote=document.createElement('p');positionNote.className='weather-note';positionNote.textContent='Move and resize this widget on the Layouts canvas, or select a tile size in the content panel.';controls.append(positionNote);
      if(onDelete){const remove=document.createElement('button');remove.type='button';remove.className='secondary';remove.textContent='Remove widget from layout';remove.onclick=()=>{onDelete();dialog.close();};controls.append(remove);}
      dialog.querySelector('button[type=submit]').textContent='Use in layout draft';
    };
    extension.output=output(layout);extension.layout=layout;
    extension.background=stage=>{
      const background=document.createElement('div');background.className='widget-wall-context';stage.prepend(background);
      const proportions=typeof wallProportions==='function'?wallProportions(layout):null;
      for(const tile of layout.tiles){
        if(tile.kind&&tile.kind!=='camera')continue;
        const cell=document.createElement('div'),image=document.createElement('img');cell.className='widget-context-cell';
        const b=proportions?proportions.bounds(tile):{left:tile.column/layout.columns,top:tile.row/layout.rows,width:tile.columnSpan/layout.columns,height:tile.rowSpan/layout.rows};
        Object.assign(cell.style,Object.fromEntries(Object.entries(b).map(([k,v])=>[k,v*100+'%'])));
        const camera=cameras.find(c=>c.slot===tile.cameraSlot);image.alt=camera?.name||'Camera '+tile.cameraSlot;
        image.style.objectFit=tile.sizing==='fill'?'cover':tile.sizing==='stretch'?'fill':'contain';
        image.style.objectPosition=(tile.horizontalPositionPercent??50)+'% '+(tile.verticalPositionPercent??50)+'%';
        image.style.transform='scale('+((tile.zoomPercent??100)/100)+')';
        image.onerror=()=>{image.remove();cell.textContent='Snapshot unavailable';};
        if(typeof dashboardUX!=='undefined')dashboardUX.snapshot(image,tile.cameraSlot);
        cell.append(image);background.append(cell);
      }
      const note=document.createElement('p');note.className='weather-note';note.textContent='Layout camera snapshots · preview scaled from '+extension.output.width+' × '+extension.output.height+'.';stage.after(note);
    };
    const args=[widget[widget.kind],(_,placement)=>onSave({...placement,id:widget.id,kind:widget.kind}),widget,null];if(widget.kind==='weather')args.push([]);args.push(extension);ui(widget.kind).editor(...args);
  }
  function add(kind,layout,cameras,onChange){if((layout.widgets||[]).length>=64){uiDialogs.toast('Keep up to 64 widgets per layout.','error');return;}const widget={id:Array.from(crypto.getRandomValues(new Uint8Array(16)),v=>v.toString(16).padStart(2,'0')).join(''),kind,hostCameraSlot:0,enabled:true,widthPercent:40,x:50,y:50,margin:12,[kind]:ui(kind).defaults()};const size=output(layout);setRectangle(widget,layout,{left:size.width-(kind==='aircraft'?652:332),top:size.height-(kind==='aircraft'?282:192),width:kind==='aircraft'?640:320,height:kind==='aircraft'?270:180});widget.cell={row:0,column:0,rowSpan:1,columnSpan:1};const empty=[];for(let row=0;row<layout.rows;row++)for(let column=0;column<layout.columns;column++)if(!layout.tiles.some(t=>row>=t.row&&row<t.row+t.rowSpan&&column>=t.column&&column<t.column+t.columnSpan)&&!(layout.widgets||[]).some(w=>w.cell&&row>=w.cell.row&&row<w.cell.row+w.cell.rowSpan&&column>=w.cell.column&&column<w.cell.column+w.cell.columnSpan))empty.push({row,column});if(empty.length)Object.assign(widget.cell,empty[0]);edit(widget,layout,cameras,value=>{(layout.widgets??=[]).push(value);onChange();});}
  function render(board,layout,cameras,onChange,selection={}){
    for(const [host,observer] of observers)if(!host.isConnected){observer.disconnect();observers.delete(host);}
    for(const widget of layout.widgets||[]){if((!widget.enabled&&!onChange)||!pluginsUi.enabled(widget.kind))continue;const host=document.createElement('div');host.className='layout-widget';host.style.cssText='position:absolute;z-index:20;touch-action:none;';host.dataset.widgetId=widget.id;host.classList.toggle('selected',selection.selectedId===widget.id);host.setAttribute('aria-pressed',String(selection.selectedId===widget.id));const card=ui(widget.kind).preview(widget[widget.kind],!!onChange,true);host.append(card);board.append(host);
      const logicalWidth=layout.outputWidth||(layout.aspectRatio==='9:16'?1080:1920),logicalHeight=layout.outputHeight||(layout.aspectRatio==='9:16'?1920:1080);
      const place=()=>{const card=host.firstElementChild;if(!card)return;const scale=board.clientWidth/logicalWidth,b=bounds(widget,layout,logicalWidth,logicalHeight),content=contentScale(widget,b);if(!b){host.hidden=true;return;}host.hidden=false;card.dataset.widgetShape=b.height>b.width*1.25?'portrait':'landscape';Object.assign(host.style,{left:b.left*scale+'px',top:b.top*scale+'px',width:b.width*scale+'px',height:b.height*scale+'px'});Object.assign(card.style,{width:b.width/content+'px',height:b.height/content+'px',transformOrigin:'top left',transform:'scale('+(scale*content)+')'});if(onChange)card.style.pointerEvents='none';requestAnimationFrame(()=>ui(widget.kind).fit?.(card));};place();const observer=new ResizeObserver(()=>{if(!host.isConnected){observer.disconnect();return;}place();});observer.observe(board);observers.set(host,observer);
      if(!onChange){host.style.pointerEvents='none';continue;}
      host.tabIndex=0;host.setAttribute('role','button');host.setAttribute('aria-label','Edit '+widget.kind+' widget');host.style.cursor='move';card.style.pointerEvents='none';
      for(const edge of ['n','s','e','w','nw','ne','sw','se']){const handle=document.createElement('span');handle.className='widget-resize widget-resize-'+edge;handle.dataset.widgetEdge=edge;handle.title='Drag to resize freely; content adapts to the tile';handle.setAttribute('aria-hidden','true');host.append(handle);}
      const open=()=>{selection.onSelect?.(widget.id);edit(widget,layout,cameras,value=>{layout.widgets=layout.widgets.map(w=>w.id===widget.id?value:w);onChange();},()=>{layout.widgets=layout.widgets.filter(w=>w.id!==widget.id);onChange();});};

      host.onkeydown=e=>{if(['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(e.key)){e.preventDefault();e.stopPropagation();const b=bounds(widget,layout,logicalWidth,logicalHeight),step=e.altKey?1:10,dx=e.key==='ArrowLeft'?-step:e.key==='ArrowRight'?step:0,dy=e.key==='ArrowUp'?-step:e.key==='ArrowDown'?step:0;if(widget.cell){const c={...widget.cell},x=dx?Math.sign(dx):0,y=dy?Math.sign(dy):0;if(e.shiftKey){c.columnSpan=Math.max(1,Math.min(layout.columns-c.column,c.columnSpan+x));c.rowSpan=Math.max(1,Math.min(layout.rows-c.row,c.rowSpan+y));}else{c.column=Math.max(0,Math.min(layout.columns-Math.min(c.columnSpan,layout.columns),c.column+x));c.row=Math.max(0,Math.min(layout.rows-Math.min(c.rowSpan,layout.rows),c.row+y));}widget.cell=c;}else setRectangle(widget,layout,e.shiftKey?{...b,width:b.width+dx,height:b.height+dy}:{...b,left:b.left+dx,top:b.top+dy});selection.onSelect?.(widget.id,false);onChange();requestAnimationFrame(()=>[...document.querySelectorAll('.layout-widget')].find(n=>n.dataset.widgetId===widget.id)?.focus());return;}if(e.key==='Delete'){e.preventDefault();selection.onRemove?.(widget.id);return;}if(e.key==='Enter'||e.key===' '){e.preventDefault();open();}};
      host.onpointerdown=e=>{if(e.button!==0)return;e.stopPropagation();e.preventDefault();host.setPointerCapture(e.pointerId);const original={...widget},edge=e.target.dataset.widgetEdge,logicalBounds=bounds(widget,layout,logicalWidth,logicalHeight),start={x:e.clientX,y:e.clientY,wx:widget.x,wy:widget.y};let moved=false;const r=region(widget,layout,board.clientWidth,board.clientHeight),b={width:host.clientWidth,height:host.clientHeight};if(!r)return;
        host.onpointermove=m=>{const dx=m.clientX-start.x,dy=m.clientY-start.y;if(!moved&&Math.hypot(dx,dy)<4)return;moved=true;if(original.cell){const g=grid(layout),rect=board.getBoundingClientRect(),at=(weights,value)=>{let sum=0;for(let i=0;i<weights.length;i++){sum+=weights[i];if(value<sum)return i;}return weights.length-1;},cx=at(g.columns,(m.clientX-rect.left)/rect.width)-at(g.columns,(start.x-rect.left)/rect.width),cy=at(g.rows,(m.clientY-rect.top)/rect.height)-at(g.rows,(start.y-rect.top)/rect.height),c={...original.cell};if(edge){if(edge.includes('e'))c.columnSpan=Math.max(1,Math.min(layout.columns-c.column,c.columnSpan+cx));if(edge.includes('s'))c.rowSpan=Math.max(1,Math.min(layout.rows-c.row,c.rowSpan+cy));if(edge.includes('w')){const right=Math.min(layout.columns,c.column+c.columnSpan);c.column=Math.max(0,Math.min(right-1,c.column+cx));c.columnSpan=right-c.column;}if(edge.includes('n')){const bottom=Math.min(layout.rows,c.row+c.rowSpan);c.row=Math.max(0,Math.min(bottom-1,c.row+cy));c.rowSpan=bottom-c.row;}}else{c.column=Math.max(0,Math.min(layout.columns-Math.min(c.columnSpan,layout.columns),c.column+cx));c.row=Math.max(0,Math.min(layout.rows-Math.min(c.rowSpan,layout.rows),c.row+cy));}widget.cell=c;place();return;}if(edge){const scale=board.clientWidth/logicalWidth;Object.assign(widget,resized(original,logicalBounds,logicalWidth,logicalHeight,dx/scale,dy/scale,edge));place();return;}const margin=Math.min(widget.margin*board.clientWidth/logicalWidth,r.width/2,r.height/2);widget.x=Math.max(0,Math.min(100,start.wx+dx/Math.max(1,r.width-b.width-2*margin)*100));widget.y=Math.max(0,Math.min(100,start.wy+dy/Math.max(1,r.height-b.height-2*margin)*100));place();};
        const stop=()=>{host.onpointermove=null;host.onpointerup=null;host.onpointercancel=null;};host.onpointerup=()=>{stop();if(moved){selection.onSelect?.(widget.id,false);onChange();}else if(!edge){if(selection.selectedId===widget.id)open();else selection.onSelect?.(widget.id);}};host.onpointercancel=()=>{stop();for(const key of Object.keys(widget))delete widget[key];Object.assign(widget,original);place();};
      };
    }
  }
  return {add,edit,render,bounds,resized,contentScale,setRectangle,inspector};
})();
