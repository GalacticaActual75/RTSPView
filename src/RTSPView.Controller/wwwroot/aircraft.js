const aircraftUi = (() => {
  const labels = {owner:'Registered owner',airline:'Operating airline (lookup)',destination:'Destination (callsign lookup)',type:'Aircraft type / registration',altitude:'Altitude',speed:'Ground speed',distance:'Distance / direction',track:'Track',verticalRate:'Climb / descent'};
  const textLabels={location:'Location heading',owner:'Registered owner',type:'Aircraft type',registration:'Tail number',callsign:'Callsign',airline:'Airline',destination:'Destination',altitude:'Altitude',speed:'Speed',distance:'Distance / direction',track:'Track',verticalRate:'Climb / descent',photoCredit:'Photo label and credit',footer:'Data source footer',status:'Empty / unavailable message'};
  const hasDetailValue=value=>typeof value==='string'&&!['','unavailable','unknown','n/a','-','—'].includes(value.trim().toLowerCase());
  const textSize=(o,key)=>o.textSizes?.[key]??(key==='owner'?o.fontSize*(o.preset==='board'?1:1.5):['photoCredit','footer'].includes(key)?10:Math.max(12,o.fontSize*(key==='status'?.65:.6)));
  const balancedTextSizes=()=>({location:12,owner:20,type:18,registration:14,callsign:14,airline:14,destination:14,altitude:16,speed:16,distance:14,track:14,verticalRate:14,photoCredit:10,footer:10,status:16});
  function sized(n,o,key){n.dataset.aircraftText=key;n.style.fontSize=textSize(o,key)+'px';n.style.lineHeight='1.25';return n;}
  const defaults = () => ({density:'auto',location:'',latitude:null,longitude:null,radiusMiles:10,minimumAltitudeFeet:null,maximumAltitudeFeet:null,preset:'featured',units:'imperial',maximumAircraft:2,cardDesign:'compact',fadeEnabled:false,fadeInMilliseconds:200,fadeOutMilliseconds:800,textSizes:{},hideWhenEmpty:false,showHeading:true,showPhoto:true,
    ...Object.fromEntries(['theme','accent','backgroundColor','backgroundOpacity','contentOpacity','fontSize','iconSize','padding','cornerRadius','alignment'].map(k=>[k,weatherUi.defaults()[k]])),fields:Object.keys(labels)});
  const el=(tag,text,cls)=>{const n=document.createElement(tag);if(text!==undefined)n.textContent=text;if(cls)n.className=cls;return n;};
  const key=o=>`${Number(o.latitude).toFixed(4)},${Number(o.longitude).toFixed(4)},${Number(o.radiusMiles).toFixed(1)}`;
  const cardinal=d=>['N','NE','E','SE','S','SW','W','NW'][Math.floor((d+22.5)/45)%8];
  function distance(o,a){const r=Math.PI/180,v=Math.sin((a.latitude-o.latitude)*r/2)**2+Math.cos(o.latitude*r)*Math.cos(a.latitude*r)*Math.sin((a.longitude-o.longitude)*r/2)**2;return 3958.7613*2*Math.asin(Math.sqrt(Math.max(0,Math.min(1,v))));}
  function bearing(o,a){const r=Math.PI/180,d=(a.longitude-o.longitude)*r;return (Math.atan2(Math.sin(d)*Math.cos(a.latitude*r),Math.cos(o.latitude*r)*Math.sin(a.latitude*r)-Math.sin(o.latitude*r)*Math.cos(a.latitude*r)*Math.cos(d))/r+360)%360;}
  const freshness=s=>!s||Date.now()-Date.parse(s.fetchedAt)>90000?'unavailable':s.refreshFailed||Date.now()-Date.parse(s.fetchedAt)>30000?'stale':'fresh';
  function nearby(o,s){return freshness(s)==='unavailable'?[]:(s.aircraft||[]).filter(a=>Date.parse(a.positionAt)<=Date.now()+5000&&Date.now()-Date.parse(a.positionAt)<=60000&&distance(o,a)<=o.radiusMiles&&(o.minimumAltitudeFeet==null||a.altitudeFeet!=null&&a.altitudeFeet>=o.minimumAltitudeFeet)&&(o.maximumAltitudeFeet==null||a.altitudeFeet!=null&&a.altitudeFeet<=o.maximumAltitudeFeet)).sort((a,b)=>distance(o,a)-distance(o,b)||a.hex.localeCompare(b.hex));}
  const number=(v,d=0)=>v==null?'—':v.toLocaleString('en-US',{minimumFractionDigits:d,maximumFractionDigits:d});
  function metric(f,a,o){const m=o.units==='metric';switch(f){
    case 'owner':return a.registeredOwner||'Unavailable';
    case 'airline':return 'Airline: '+(a.airline||'Unavailable');
    case 'destination':return 'Destination (lookup): '+(a.destination||'Unavailable');
    case 'type':return [a.modelName||a.type,a.registration].filter(Boolean).join(' · ');
    case 'altitude':return 'ALT '+number(a.altitudeFeet==null?null:a.altitudeFeet*(m?.3048:1))+(m?' m':' ft');
    case 'speed':return 'SPD '+number(a.speedKnots==null?null:a.speedKnots*(m?1.852:1))+(m?' km/h':' kt');
    case 'distance':return number(distance(o,a)*(m?1.609344:1),1)+(m?' km ':' mi ')+cardinal(bearing(o,a));
    case 'track':return 'TRK '+number(a.trackDegrees)+(a.trackDegrees==null?'':'° '+cardinal(a.trackDegrees));
    case 'verticalRate':return 'V/S '+(a.verticalRate>0?'+':'')+number(a.verticalRate==null?null:a.verticalRate*(m?.00508:1),m?1:0)+(m?' m/s':' ft/min');
    default:return '';
  }}
  function validPhoto(p){try{const u=new URL(p.url),l=new URL(p.link);const hosts=(p.source||'Planespotters.net')==='Planespotters.net'?u.hostname==='t.plnspttrs.net'&&l.hostname==='www.planespotters.net':p.source==='Airport-Data.com'?['airport-data.com','www.airport-data.com'].includes(u.hostname)&&['airport-data.com','www.airport-data.com'].includes(l.hostname):p.source==='Wikimedia Commons'&&p.representative&&['upload.wikimedia.org','thumb.wikimedia.org'].includes(u.hostname)&&l.hostname==='commons.wikimedia.org'&&typeof p.license==='string'&&p.license.length>0;return hosts&&u.protocol==='https:'&&!u.port&&!u.username&&!u.password&&l.protocol==='https:'&&!l.port&&!l.username&&!l.password&&typeof p.photographer==='string'&&p.photographer.length>0;}catch{return false;}}
  function sample(o){return {fetchedAt:new Date().toISOString(),aircraft:[['UAL123','B738','N123EX',12400,285],['ASA456','B39M','N456EX',18200,340],['N789EX','C172','N789EX',2200,95]].map((a,i)=>({registeredOwner:'Example owner',airline:'Example airline',destination:'SEA · Seattle (sample)',hex:'sample'+i,callsign:a[0],type:a[1],registration:a[2],altitudeFeet:a[3],speedKnots:a[4],trackDegrees:245,verticalRate:640,latitude:Number(o.latitude||0)+.005*(i+1),longitude:Number(o.longitude||0)+.005*(i+1),positionAt:new Date().toISOString()}))};}
  let snapshots=[],polling=false,fetching=false;const views=new Map(),statuses=new Map();
  function replacementState(o,s){
    if(!s)return {active:false,text:'Waiting for aircraft data. Apply this layout to start its feed.'};
    const state=freshness(s),matches=nearby(o,s).length;
    if(state!=='fresh')return {active:false,text:'Camera shown · '+(s.lastError||'Aircraft data is '+state+'.')+(s.nextRetryAt?' Retrying after '+new Date(s.nextRetryAt).toLocaleTimeString()+'.':'')};
    if(matches)return {active:true,text:`Live feed · ${matches} matching aircraft within ${o.radiusMiles} miles.`};
    const unfiltered=nearby({...o,minimumAltitudeFeet:null,maximumAltitudeFeet:null},s).length;
    return {active:false,text:unfiltered?`Camera shown · ${unfiltered} nearby aircraft excluded by your altitude filters.`:`Camera shown · No aircraft reported within ${o.radiusMiles} miles by ADSB.lol.`};
  }
  function status(options){const node=el('p',undefined,'aircraft-live-status');node.setAttribute('role','status');statuses.set(node,structuredClone(options));paintStatus(node,options);refresh();return node;}
  function paintStatus(node,o){const result=replacementState(o,snapshots.find(s=>s.key===key(o)));node.textContent=result.text;node.dataset.active=result.active;}
  function fit(node){
    if(!node.isConnected)return;
    const entry=views.get(node);if(!entry)return;
    const v=widgetViewport.apply(node,entry.options);widgetViewport.reset(node);
    for(const photo of node.querySelectorAll('.aircraft-photo'))if(photo.dataset.photoState!=='loaded'){photo.hidden=true;delete photo.dataset.responsiveHidden;}
    const hide=n=>{n.hidden=true;n.dataset.responsiveHidden='';};
    for(const n of node.querySelectorAll('[data-aircraft-text]'))n.style.fontSize=(n.dataset.aircraftText==='owner'?v.heading:['footer','photoCredit'].includes(n.dataset.aircraftText)?10:v.font)+'px';
    if(v.detailLevel===0)for(const n of node.querySelectorAll('.weather-location,[data-aircraft-text=type],[data-aircraft-text=registration],[data-aircraft-text=registeredOwner],[data-aircraft-text=callsign],[data-aircraft-text=speed],[data-aircraft-text=track],[data-aircraft-text=verticalRate]'))hide(n);
    if(v.detailLevel<2)for(const n of node.querySelectorAll('[data-aircraft-text=airline],[data-aircraft-text=destination]'))hide(n);
    const title=node.querySelector('.weather-location'),footer=node.querySelector('.weather-credit');
    const reserved=(title&&!title.hidden?title.offsetHeight+3:0)+(footer?.offsetHeight||0)+6;
    for(const flight of node.querySelectorAll('.aircraft-flight')){
      const previous=flight.querySelector('.aircraft-readings');if(previous)previous.replaceWith(...previous.children);
      flight.style.height='';flight.style.gridTemplateColumns='';flight.style.gridTemplateRows='';delete flight.dataset.photoLayout;
      const budget=Math.max(0,node.clientHeight-2*v.padding-reserved),picture=flight.querySelector('.aircraft-photo');
      let textHost=flight,textBudget=budget;
      if(picture?.dataset.photoState==='loaded'){
        const layout=widgetViewport.aircraftPhoto(flight.clientWidth,budget,entry.options.cardDesign==='photo');
        const credit=picture.querySelector('a'),img=picture.querySelector('img');credit.style.fontSize='10px';
        picture.style.width=layout.photoWidth+'px';
        const imageHeight=layout.photoHeight-credit.offsetHeight-3;
        if(layout.visible&&imageHeight>=32){
          const readings=el('div',undefined,'aircraft-readings');for(const child of [...flight.children])if(child!==picture)readings.append(child);flight.prepend(readings);
          textHost=readings;textBudget=layout.textHeight;flight.style.height=budget+'px';flight.dataset.photoLayout=layout.columns?'columns':'rows';
          flight.style.gridTemplateColumns=layout.columns?layout.textWidth+'px 8px '+layout.photoWidth+'px':'minmax(0,1fr)';
          flight.style.gridTemplateRows=layout.columns?'minmax(0,1fr)':layout.textHeight+'px 8px '+layout.photoHeight+'px';const ratio=img.naturalWidth/Math.max(1,img.naturalHeight),photoWidth=Math.min(layout.photoWidth,imageHeight*ratio);img.style.width=photoWidth+'px';img.style.height=photoWidth/ratio+'px';img.style.borderRadius=Math.max(6,Math.min(18,Math.min(photoWidth,photoWidth/ratio)*.075))+'px';
        }else hide(picture);
      }
      // Flatten metric rows so altitude and distance survive optional fields independently.
      const metrics=flight.querySelector('.aircraft-metrics');if(metrics)while(metrics.firstChild)flight.insertBefore(metrics.firstChild,metrics);metrics?.remove();
      const rank=n=>n.matches('.weather-current')?100:n.dataset.aircraftText==='altitude'?95:n.dataset.aircraftText==='distance'?90:n.dataset.aircraftText==='speed'?80:n.dataset.aircraftText==='type'?92:n.dataset.aircraftText==='registeredOwner'?85:n.dataset.aircraftText==='registration'?65:n.matches('.aircraft-photo')?(entry.options.cardDesign==='photo'?75:45):20;
      widgetViewport.fitRows(textHost,textBudget,rank);
    }
    widgetViewport.fitRows(node,node.clientHeight-2*v.padding,n=>n.matches('.aircraft-flights,.weather-condition')?100:n===footer?95:n===title?35:20);
  }
  const resize=new ResizeObserver(entries=>entries.forEach(({target})=>{const entry=views.get(target);if(entry)paint(target,entry);fit(target);}));
  const shouldHide=(o,state,count,overlay,takeover)=>(!!takeover||!!overlay&&!!o.hideWhenEmpty)&&(state!=='fresh'||count===0);
  function selectFlights(state,nearby,requestedCount,now=Date.now()){
    const count=Math.max(1,Math.min(2,requestedCount||1));
    if(!nearby.length){state.ids=[];state.page=0;state.changed=null;return [];}
    if(state.changed==null||now-state.changed>=20000){state.page=state.changed==null?0:(state.page+1)%Math.ceil(nearby.length/count);state.ids=nearby.slice(state.page*count,state.page*count+count).map(a=>a.hex);state.changed=now;}
    const chosen=state.ids.map(id=>nearby.find(a=>a.hex===id)).filter(Boolean).slice(0,count);
    for(const a of nearby)if(chosen.length<count&&!chosen.some(b=>b.hex===a.hex))chosen.push(a);
    state.ids=chosen.map(a=>a.hex);return chosen;
  }
  function setVisible(node,entry,show){
    const o=entry.options,enabled=o.fadeEnabled&&!entry.sampleAllowed;
    if(enabled&&entry.shown===show)return;
    const first=entry.shown==null,from=first?0:Number(getComputedStyle(node).opacity);entry.shown=show;entry.animation?.cancel();entry.animation=null;
    const duration=show?o.fadeInMilliseconds:o.fadeOutMilliseconds;
    if(!enabled||!duration||first&&!show){node.style.opacity=show?'1':'0';node.style.visibility=show?'visible':'hidden';return;}
    node.style.visibility='visible';node.style.opacity=show?'1':'0';
    const animation=node.animate([{opacity:from},{opacity:show?1:0}],{duration,easing:'ease-out'});entry.animation=animation;
    animation.onfinish=()=>{if(entry.animation!==animation)return;entry.animation=null;node.style.visibility=show?'visible':'hidden';};
  }
  function paint(node,entry){const o=entry.options,actual=snapshots.find(s=>s.key===key(o)),s=actual||(entry.sampleAllowed?sample(o):null),state=freshness(s),all=nearby(o,s);
    const show=entry.sampleAllowed||!shouldHide(o,state,all.length,entry.overlay,entry.takeover);setVisible(node,entry,show);if(!show)return;
    const requested=(o.cardDesign==='board'||o.preset==='board')&&node.clientWidth>=600&&node.clientHeight>=240?2:1;
    const selected=selectFlights(entry,all,requested);
    const renderKey=JSON.stringify([o,state,all,s?.lastError,!!actual,selected.map(a=>a.hex),Math.floor(Date.now()/65000)]);if(entry.renderKey===renderKey)return;entry.renderKey=renderKey;
    const oldImages=new Map();for(const img of node.querySelectorAll('.aircraft-photo img')){const url=img.getAttribute('src');if(!oldImages.has(url))oldImages.set(url,[]);oldImages.get(url).push(img);}
    node.replaceChildren();node.dataset.theme=o.theme;node.dataset.preset=o.preset;node.dataset.design=o.cardDesign||'compact';
    weatherUi.applyAppearance(node,o);
    if(o.showHeading!==false)node.append(sized(el('div',(o.location||'Your location')+' · Nearby aircraft','weather-location'),o,'location'));
    if(state==='unavailable'){node.append(sized(el('div','Aircraft data unavailable','weather-condition'),o,'status'));if(s?.lastError)node.append(sized(el('div',s.lastError,'aircraft-feed-error'),o,'status'));}
    else if(!all.length)node.append(sized(el('div',state==='stale'?'Waiting for fresh positions':'No aircraft nearby','weather-condition'),o,'status'));
    else {
      const board=el('div',undefined,'aircraft-flights');board.dataset.columns=selected.length>1?'2':'1';node.append(board);
      for(const a of selected){
        const flight=el('div',undefined,'aircraft-flight'),heading=el('div',undefined,'weather-current'),icon=el('span','✈','weather-icon');
        const owner=sized(el('strong',a.callsign||a.registration||a.hex.toUpperCase()),o,'owner');owner.title=owner.textContent;heading.append(icon,owner);flight.append(heading);
        if(o.fields.includes('type'))flight.append(sized(el('div',a.modelName||a.type||'Aircraft type unavailable','weather-highlow'),o,'type'));
        if(o.fields.includes('owner')&&hasDetailValue(a.registeredOwner))flight.append(sized(el('div',a.registeredOwner,'weather-highlow aircraft-metric'),o,'registeredOwner'));
        if(a.registration&&a.callsign&&a.registration!==a.callsign)flight.append(sized(el('div',a.registration,'weather-highlow'),o,'registration'));
        if(a.registeredOwner&&a.callsign&&a.callsign!==a.registration)flight.append(sized(el('div',a.callsign,'weather-highlow aircraft-metric aircraft-callsign'),o,'callsign'));
        for(const f of o.fields.filter(f=>['airline','destination'].includes(f)&&hasDetailValue(a[f]))){const detail=el('div',metric(f,a,o),'weather-highlow aircraft-metric aircraft-detail');detail.title=detail.textContent;flight.append(sized(detail,o,f));}
        const body=el('div',undefined,'aircraft-body'),data=el('div',undefined,'aircraft-data');for(const detail of [...flight.children].slice(1))data.append(detail);body.append(data);flight.append(body);
        const metrics=el('div',undefined,'aircraft-metrics aircraft-metric');for(const f of o.fields.filter(f=>!['type','owner','airline','destination'].includes(f)))metrics.append(sized(el('div',metric(f,a,o),'weather-highlow'),o,f));flight.append(metrics);
        if(o.showPhoto&&a.photo&&validPhoto(a.photo)){
          const p=a.photo,figure=el('div',undefined,'aircraft-photo'),image=oldImages.get(p.url)?.shift()||el('img');
          image.alt='Aircraft photo';image.referrerPolicy='no-referrer';figure.hidden=true;figure.dataset.photoState='pending';
          const ready=()=>{const loaded=image.complete&&image.naturalWidth>0;figure.dataset.photoState=loaded?'loaded':'pending';figure.hidden=!loaded;body.classList.toggle('has-photo',loaded);requestAnimationFrame(()=>fit(node));};
          image.onerror=()=>{image._retryAt=Date.now()+65000;figure.dataset.photoState='failed';figure.hidden=true;delete figure.dataset.responsiveHidden;body.classList.remove('has-photo');requestAnimationFrame(()=>fit(node));};image.onload=ready;
          const credit=el('a',(p.representative?'Representative · ':'')+'Photo © '+p.photographer+' · '+(p.source||'Planespotters.net')+(p.license?' · '+p.license:''));credit.href=p.link;credit.target='_blank';credit.rel='noopener noreferrer';
          figure.append(image,sized(credit,o,'photoCredit'));body.append(figure);
          if(image.getAttribute('src')!==p.url||image.complete&&!image.naturalWidth&&Date.now()>=(image._retryAt||0))image.src=p.url;ready();
        }
        // One priority flow per aircraft; metadata never owns a reserved photo column.
        const picture=body.querySelector('.aircraft-photo');
        const identity=[...data.children];
        body.remove();metrics.remove();
        const values=[...metrics.children];
        values.sort((a,b)=>['altitude','distance','speed','track','verticalRate'].indexOf(a.dataset.aircraftText)-['altitude','distance','speed','track','verticalRate'].indexOf(b.dataset.aircraftText));
        flight.append(...values,...identity);if(picture)flight.append(picture);
        board.append(flight);
      }
    }
    const shown=selected.length;
    const credit=el('a',(entry.sampleAllowed&&!actual?'Sample aircraft · ':'')+(state==='stale'?'Outdated · ':state==='unavailable'?'Unavailable · ':'')+(all.length>shown?`+${all.length-shown} nearby · `:'')+'ADSB.lol · ODbL 1.0'+' · adsbdb'+(selected.some(a=>a.detailsSource==='FAA')?' · FAA':''),'weather-credit');credit.href='https://www.adsb.lol/docs/open-data/api/';credit.target='_blank';credit.rel='noopener noreferrer';credit.dataset.label=credit.textContent;node.append(sized(credit,o,'footer'));requestAnimationFrame(()=>fit(node));
  }
  function preview(options,sampleAllowed=false,overlay=false,takeover=false){const node=el('div',undefined,'weather-card aircraft-card'),entry={options:structuredClone(options),sampleAllowed,overlay,takeover,featured:null,selectedAt:0};views.set(node,entry);paint(node,entry);resize.observe(node);return node;}
  async function refresh(){if(typeof pluginsUi!=='undefined'&&!pluginsUi.enabled('aircraft'))return;polling=true;if(fetching)return;fetching=true;try{snapshots=await api('/api/aircraft');}catch{/* Per-position age continues to expire even on an outage. */}finally{fetching=false;for(const [node,entry] of views){if(!node.isConnected){resize.unobserve(node);views.delete(node);}else paint(node,entry);}for(const [node,o] of statuses){if(!node.isConnected)statuses.delete(node);else paintStatus(node,o);}}}
  setInterval(()=>{if(polling&&document.visibilityState==='visible')refresh();},5000);
  function overlayBounds(overlay,width,height){const o=overlay.aircraft,margin=Math.min(overlay.margin,width/2,height/2),available=Math.max(0,width-margin*2),availableHeight=Math.max(0,height-margin*2),w=available*overlay.widthPercent/100,size=Math.min(o.fontSize,Math.max(12,(w-o.padding*2)/5)),rows=1,flightWidth=(w-o.padding*2)/(o.cardDesign==='board'?2:o.preset==='board'?Math.min(2,o.maximumAircraft):1),columns=flightWidth>=400?3:flightWidth>=220?2:1,lines=o.fields.filter(f=>['type','owner','airline','destination'].includes(f)).length+Math.ceil(o.fields.filter(f=>!['type','owner','airline','destination'].includes(f)).length/columns),h=Math.min(availableHeight,Math.ceil(o.padding*2+42+(o.showPhoto&&flightWidth>=150?24:0)+rows*(Math.max(size*1.7,Math.min(o.iconSize,(w-o.padding*2)*.2))+8+lines*(Math.max(12,size*.6)*1.4+2))));let typographyHeight=Object.keys(o.textSizes||{}).length?Math.min(availableHeight,Math.ceil(o.padding*2+textSize(o,'location')*1.25+textSize(o,'footer')*1.25+Math.max(o.iconSize,textSize(o,'owner')*2.5)+18+textSize(o,'registration')*1.25+o.fields.filter(f=>f!=='owner').reduce((sum,f)=>sum+textSize(o,f)*1.25+2,0))):h;if(o.showPhoto&&['photo','board'].includes(o.cardDesign))typographyHeight=Math.min(availableHeight,typographyHeight+144);return {width:w,height:typographyHeight,left:margin+(available-w)*overlay.x/100,top:margin+(availableHeight-typographyHeight)*overlay.y/100};}
  function editor(initial,onSave,overlay=null,backgroundSlot=null,widgetContext=null){
    const opener=document.activeElement,o={...defaults(),...structuredClone(initial)},placement=overlay?structuredClone(overlay):null;
    o.maximumAircraft=Math.min(2,o.maximumAircraft);
    if(o.preset==='board'&&o.cardDesign!=='photo')o.cardDesign='board';
    if(o.cardDesign==='data')o.cardDesign='compact';
    o.preset='featured';
    const dialog=el('dialog',undefined,'weather-editor aircraft-editor'),form=el('form'),body=el('div',undefined,'weather-editor-body'),left=el('section'),controls=el('section');
    form.append(el('h2',overlay?'Aircraft Widget':'Aircraft tile'),el('p',overlay?'Save & apply updates this camera’s aircraft widget in every layout that shows the camera.':'Use in layout draft changes this draft. Save or apply the layout to persist it.','weather-note'),body);body.append(left,controls);dialog.append(form);
    const stage=el('div',undefined,'weather-editor-preview'),host=el('div',undefined,'weather-preview-host');left.append(stage);if(placement)stage.style.aspectRatio=widgetContext?.output?widgetContext.output.width+'/'+widgetContext.output.height:'16/9';
    if(widgetContext?.background)widgetContext.background(stage);
    if(backgroundSlot){const image=el('img');image.alt='Camera snapshot';dashboardUX.snapshot(image,backgroundSlot);stage.append(image);}stage.append(host);
    left.append(el('p','Sample aircraft only demonstrate the design and never trigger camera replacement. Live traffic comes from ADSB.lol; its coverage may differ from Flightradar24. Cards show at most two aircraft and rotate every 20 seconds, nearest first. Small cards omit details that do not fit.','weather-note'));
    const status=el('p','','weather-editor-state');status.setAttribute('role','status');
    function paintPreview(){host.replaceChildren(preview(o,true));if(placement){const outputWidth=widgetContext?.output?.width||640,outputHeight=widgetContext?.output?.height||360,scale=stage.clientWidth/outputWidth,b=widgetContext?layoutWidgetsUi.bounds({...placement,aircraft:o},widgetContext.layout,outputWidth,outputHeight):overlayBounds({...placement,aircraft:o},640,360);Object.assign(host.style,{position:'absolute',left:b.left*scale+'px',top:b.top*scale+'px',width:b.width*scale+'px',height:b.height*scale+'px',visibility:placement.enabled?'visible':'hidden'});Object.assign(host.firstElementChild.style,{width:b.width/(widgetContext?layoutWidgetsUi.contentScale({...placement,aircraft:o},b):1)+'px',height:b.height/(widgetContext?layoutWidgetsUi.contentScale({...placement,aircraft:o},b):1)+'px',transformOrigin:'top left',transform:`scale(${scale*(widgetContext?layoutWidgetsUi.contentScale({...placement,aircraft:o},b):1)})`});}}
    function field(label,control,parent=controls){const n=el('label',label);control.setAttribute('aria-label',label);if(control.type==='checkbox'){n.className='weather-toggle';const track=el('span',undefined,'switch');control.setAttribute('role','switch');track.append(control,el('span'));n.append(track);}else n.append(control);parent.append(n);return control;}
    function button(label,action,parent=controls){const n=el('button',label,'secondary');n.type='button';n.onclick=action;parent.append(n);return n;}
    const inputs={};
    function input(label,name,type='text',parent=controls,min,max,optional=false,target=o){const n=el('input');n.type=type;n.value=target[name]??'';if(min!==undefined)n.min=min;if(max!==undefined)n.max=max;if(type==='number')n.step=['latitude','longitude'].includes(name)?'any':name==='radiusMiles'?'.1':'1';n.required=!optional&&type!=='range';n.oninput=()=>{target[name]=['number','range'].includes(type)?(n.value===''?null:Number(n.value)):n.value;paintPreview();};inputs[name]=n;return field(label,n,parent);}
    function select(label,name,choices,parent=controls){const n=el('select');for(const [value,text] of choices)n.add(new Option(text,value));n.value=o[name];n.onchange=()=>{o[name]=n.value;paintPreview();};return field(label,n,parent);}
    function toggle(label,name,target=o,parent=controls){const n=el('input');n.type='checkbox';n.checked=target[name];n.onchange=()=>{target[name]=n.checked;paintPreview();};field(label,n,parent);}
    const sliderOutputs={};
    function slider(label,name,parent,min,max,target=o){const n=input(label,name,'range',parent,min,max,false,target),value=el('output',String(target[name]));n.step='1';n.parentElement.append(value);sliderOutputs[name]=value;const update=n.oninput;n.oninput=()=>{update();value.textContent=n.value;};}
    if(placement)toggle('Enable Aircraft Widget','enabled',placement);
    input('Location name','location').maxLength=80;
    const search=el('input');search.type='search';search.placeholder='City or place';field('Find a location',search);
    const results=el('div',undefined,'weather-search-results');controls.append(results);
    button('Search',async()=>{results.replaceChildren();try{const found=await api('/api/aircraft/search?q='+encodeURIComponent(search.value));for(const place of found.results||[])button([place.name,place.admin1,place.country].filter(Boolean).join(', '),()=>{o.location=place.name;o.latitude=place.latitude;o.longitude=place.longitude;for(const k of ['location','latitude','longitude'])inputs[k].value=o[k];results.replaceChildren();paintPreview();},results);if(!results.children.length)results.append(el('p','No matching locations.'));}catch(e){status.textContent=e.message;}});
    if(typeof pluginsUi==='undefined'||pluginsUi.enabled('weather'))button('Use weather location',async()=>{try{const c=await api('/api/config'),locations=[...(c.weatherOverlays||[]).map(w=>w.weather),...(c.layouts||[]).flatMap(l=>[...(l.widgets||[]).filter(w=>w.kind==='weather').map(w=>w.weather),...l.tiles.filter(t=>t.kind==='weather').map(t=>t.weather)])];results.replaceChildren();if(!locations.length){status.textContent='No saved weather location. Search above or enter coordinates.';return;}for(const w of locations)button(w.location,()=>{for(const k of ['location','latitude','longitude']){o[k]=w[k];inputs[k].value=w[k];}results.replaceChildren();paintPreview();},results);}catch(e){status.textContent=e.message;}});
    const coordinates=el('details');coordinates.append(el('summary','Enter coordinates instead'));controls.append(coordinates);
    input('Latitude','latitude','number',coordinates,-90,90);input('Longitude','longitude','number',coordinates,-180,180);
    input('Search radius (miles)','radiusMiles','number',controls,1,100);
    const filters=el('details');filters.append(el('summary','Altitude filters'));controls.append(filters);
    input('Minimum altitude (ft, optional)','minimumAltitudeFeet','number',filters,-2000,100000,true);input('Maximum altitude (ft, optional)','maximumAltitudeFeet','number',filters,-2000,100000,true);
    select('Presentation','cardDesign',[['compact','Responsive flight card'],['photo','Photo emphasis'],['board','Two flights when space allows']]);
    select('Units','units',[['imperial','Feet · knots · miles'],['metric','Meters · km/h · kilometers']]);
    toggle('Show location heading','showHeading');
    toggle('Show aircraft photo when available','showPhoto');
    const fades=el('details');fades.append(el('summary','Fade transitions'));controls.append(fades);
    toggle('Fade when appearing or disappearing','fadeEnabled',o,fades);
    slider('Fade-in duration (ms)','fadeInMilliseconds',fades,0,5000);
    slider('Fade-out duration (ms)','fadeOutMilliseconds',fades,0,5000);
    fades.append(el('p','Suggested: 200 ms in, 800 ms out. Fades apply when the card appears or hides, not on each data refresh.','weather-note'));
    if(placement){toggle('Hide when no aircraft nearby','hideWhenEmpty');controls.append(el('p','Off keeps the widget visible like weather. On shows it only while fresh aircraft positions match your radius and altitude filters. Waiting, empty, stale and unavailable states stay hidden.','weather-note'));}
    const fields=el('details');fields.append(el('summary','Choose aircraft information'));controls.append(fields);for(const [id,label] of Object.entries(labels)){const n=el('input');n.type='checkbox';n.checked=o.fields.includes(id);n.onchange=()=>{o.fields=n.checked?[...o.fields,id]:o.fields.filter(f=>f!==id);paintPreview();};field(label,n,fields);}
    select('Information density','density',[['auto','Auto · adapt to tile'],['minimal','Minimal'],['standard','Standard'],['detailed','Detailed when space allows']]);
    const appearance=el('details');appearance.append(el('summary','Advanced appearance'));controls.append(appearance);
    select('Theme','theme',[['auto','Automatic (RTSPView dark)'],['dark','Dark'],['light','Light']],appearance);select('Text alignment','alignment',[['left','Left'],['center','Center'],['right','Right']],appearance);input('Accent color','accent','color',appearance);
    const background=input('Background color','backgroundColor','color',appearance);background.value=o.backgroundColor||(o.theme==='light'?'#F0F4F9':'#12161D');
    button('Use theme background',()=>{o.backgroundColor=null;background.value=o.theme==='light'?'#F0F4F9':'#12161D';paintPreview();},appearance);
    for(const [label,id,min,max] of [['Background opacity (%)','backgroundOpacity',0,100],['Content opacity (%)','contentOpacity',0,100]])slider(label,id,appearance,min,max);
    if(placement){const position=el('details');position.open=true;position.append(el('summary','Position and size'));controls.append(position);const corners=el('div',undefined,'weather-corners');position.append(corners);for(const [label,x,y] of [['Top left',0,0],['Top right',100,0],['Bottom left',0,100],['Bottom right',100,100],['Center',50,50]])button(label,()=>{placement.x=x;placement.y=y;inputs.x.value=x;inputs.y.value=y;sliderOutputs.x.textContent=x;sliderOutputs.y.textContent=y;paintPreview();},corners);for(const [label,id,min,max] of [['Maximum width (%)','widthPercent',15,95],['Horizontal position (%)','x',0,100],['Vertical position (%)','y',0,100],['Edge margin','margin',0,80]])slider(label,id,position,min,max,placement);}
    controls.append(el('p','ADSB.lol receives the search coordinates and radius. Refreshes about every 10 seconds when available, with slower retries after errors. Photos, when enabled, use Planespotters.net by aircraft identifier or registration, then Airport-Data.com, with Wikimedia Commons model/airline photos as a labelled fallback. Owner, airline and destination lookups use adsbdb. Destinations are callsign database matches and may differ from the current flight plan.','weather-note'));
    const actions=el('div',undefined,'weather-editor-actions');form.append(status,actions);button('Cancel',()=>dialog.close(),actions);const save=el('button',overlay?'Save & apply':'Use in layout draft');save.type='submit';actions.append(save);
    form.onsubmit=async e=>{e.preventDefault();if(!o.location.trim()||!Number.isFinite(o.latitude)||!Number.isFinite(o.longitude)){status.textContent='Enter a location name and valid coordinates.';return;}if(o.minimumAltitudeFeet!=null&&o.maximumAltitudeFeet!=null&&o.minimumAltitudeFeet>o.maximumAltitudeFeet){status.textContent='Minimum altitude must not exceed maximum altitude.';return;}save.disabled=true;try{await onSave(o,placement?{...placement,aircraft:o}:null);dialog.close();}catch(error){status.textContent=error.message;}finally{save.disabled=false;}};
    if(widgetContext)widgetContext(controls,placement,dialog,paintPreview);
    const observer=new ResizeObserver(paintPreview);observer.observe(stage);dialog.onclose=()=>{observer.disconnect();dialog.remove();if(opener?.isConnected)opener.focus();};document.body.append(dialog);dialog.showModal();paintPreview();inputs.location.focus();refresh();
  }
  async function overlayEditor(slot){try{const config=await api('/api/config'),existing=(config.aircraftOverlays||[]).find(o=>o.hostCameraSlot===slot)||{hostCameraSlot:slot,enabled:true,widthPercent:40,x:100,y:100,margin:12,aircraft:{...defaults(),fields:["type","owner","airline","altitude","speed","distance"]}};editor(existing.aircraft,async(_,overlay)=>{const saved=await api('/api/aircraft/overlays/'+slot,{method:'PUT',body:JSON.stringify(overlay)});window.dispatchEvent(new CustomEvent('aircraft-overlay-saved',{detail:saved}));},existing,slot);}catch(e){uiDialogs.toast(e.message,'error');}}
  return {fit,setVisible,hasDetailValue,textSize,balancedTextSizes,selectFlights,defaults,preview,editor,overlayEditor,overlayBounds,refresh,nearby,metric,shouldHide,status,replacementState};
})();
