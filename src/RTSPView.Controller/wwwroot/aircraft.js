const aircraftUi = (() => {
  const labels = {owner:'Registered owner',airline:'Operating airline (lookup)',destination:'Destination (callsign lookup)',type:'Aircraft type / registration',altitude:'Altitude',speed:'Ground speed',distance:'Distance / direction',track:'Track',verticalRate:'Climb / descent'};
  const defaults = () => ({location:'',latitude:null,longitude:null,radiusMiles:10,minimumAltitudeFeet:null,maximumAltitudeFeet:null,preset:'featured',units:'imperial',maximumAircraft:5,hideWhenEmpty:false,showPhoto:true,
    ...Object.fromEntries(['theme','accent','backgroundOpacity','fontSize','iconSize','padding','cornerRadius','alignment'].map(k=>[k,weatherUi.defaults()[k]])),fields:Object.keys(labels)});
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
    case 'type':return [a.type,a.registration].filter(Boolean).join(' · ');
    case 'altitude':return 'ALT '+number(a.altitudeFeet==null?null:a.altitudeFeet*(m?.3048:1))+(m?' m':' ft');
    case 'speed':return 'SPD '+number(a.speedKnots==null?null:a.speedKnots*(m?1.852:1))+(m?' km/h':' kt');
    case 'distance':return number(distance(o,a)*(m?1.609344:1),1)+(m?' km ':' mi ')+cardinal(bearing(o,a));
    case 'track':return 'TRK '+number(a.trackDegrees)+(a.trackDegrees==null?'':'° '+cardinal(a.trackDegrees));
    case 'verticalRate':return 'V/S '+(a.verticalRate>0?'+':'')+number(a.verticalRate==null?null:a.verticalRate*(m?.00508:1),m?1:0)+(m?' m/s':' ft/min');
    default:return '';
  }}
  function validPhoto(p){try{const u=new URL(p.url),l=new URL(p.link);return u.protocol==='https:'&&u.hostname==='t.plnspttrs.net'&&!u.port&&!u.username&&l.protocol==='https:'&&l.hostname==='www.planespotters.net'&&!l.port&&!l.username&&typeof p.photographer==='string'&&p.photographer.length>0;}catch{return false;}}
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
  function fit(node){if(!node.isConnected)return;const rows=[...node.querySelectorAll('.aircraft-flight')];for(const n of node.querySelectorAll('[data-aircraft-hidden]')){n.hidden=false;delete n.dataset.aircraftHidden;}const footer=node.querySelector('.weather-credit');if(!footer)return;footer.textContent=footer.dataset.label;
    let omitted=false;const optional=[...node.querySelectorAll('.aircraft-metric')];while(node.scrollHeight>node.clientHeight+1&&optional.length){const n=optional.pop();n.hidden=true;n.dataset.aircraftHidden='';omitted=true;}
    if(rows.length){const details=[...rows[0].querySelectorAll('.aircraft-metric:not(.aircraft-photo)'),...rows[0].querySelectorAll('.aircraft-photo')];while(node.scrollHeight>node.clientHeight+1&&details.length){const n=details.pop();n.hidden=true;n.dataset.aircraftHidden='';omitted=true;}}
    const hiddenFlights=node.querySelectorAll('.aircraft-flight[data-aircraft-hidden]').length;if(hiddenFlights)footer.textContent=hiddenFlights+' more hidden · '+footer.textContent;if(omitted)footer.textContent='Details hidden · '+footer.textContent;node.title=omitted?'Enlarge the aircraft tile or widget to show more details.':'';
  }
  const resize=new ResizeObserver(entries=>entries.forEach(e=>fit(e.target)));
  const shouldHide=(o,state,count,overlay,takeover)=>(!!takeover||!!overlay&&!!o.hideWhenEmpty)&&(state!=='fresh'||count===0);
  function paint(node,entry){const o=entry.options,actual=snapshots.find(s=>s.key===key(o)),s=actual||(entry.sampleAllowed?sample(o):null),state=freshness(s),all=nearby(o,s);
    const renderKey=JSON.stringify([o,state,all,s?.lastError,!!actual,Math.floor(Date.now()/20000)]);if(entry.renderKey===renderKey)return;entry.renderKey=renderKey;
    const oldImages=new Map([...node.querySelectorAll('.aircraft-photo img')].map(img=>[img.getAttribute('src'),img]));
    node.replaceChildren();node.dataset.theme=o.theme;node.dataset.preset=o.preset;
    weatherUi.applyAppearance(node,o);
    node.style.visibility=shouldHide(o,state,all.length,entry.overlay,entry.takeover)?'hidden':'visible';
    node.append(el('div',(o.location||'Your location')+' · Nearby aircraft','weather-location'));
    if(state==='unavailable'){node.append(el('div','Aircraft data unavailable','weather-condition'));if(s?.lastError)node.append(el('div',s.lastError,'aircraft-feed-error'));}
    else if(!all.length)node.append(el('div',state==='stale'?'Waiting for fresh positions':'No aircraft nearby','weather-condition'));
    else {
      if(!all.some(a=>a.hex===entry.featured)||Date.now()-entry.selectedAt>=20000){entry.featured=all[0].hex;entry.selectedAt=Date.now();}
      const selected=o.preset==='board'?all.slice(0,o.maximumAircraft):[all.find(a=>a.hex===entry.featured)];
      const board=el('div',undefined,'aircraft-flights');board.dataset.columns=selected.length>1?'2':'1';node.append(board);
      for(const a of selected){
        const flight=el('div',undefined,'aircraft-flight'),heading=el('div',undefined,'weather-current'),icon=el('span','✈','weather-icon');
        heading.append(icon,el('strong',a.registeredOwner||a.callsign||a.registration||a.hex.toUpperCase()));flight.append(heading);
        if(o.fields.includes('type'))flight.append(el('div',a.type||'Aircraft type unavailable','weather-highlow'));
        if(a.registration)flight.append(el('div',a.registration,'weather-highlow'));
        if(a.registeredOwner&&a.callsign&&a.callsign!==a.registration)flight.append(el('div',a.callsign,'weather-highlow aircraft-metric aircraft-callsign'));
        for(const f of o.fields.filter(f=>['airline','destination'].includes(f))){const detail=el('div',metric(f,a,o),'weather-highlow aircraft-metric aircraft-detail');detail.title=detail.textContent;flight.append(detail);}
        const metrics=el('div',undefined,'aircraft-metrics aircraft-metric');for(const f of o.fields.filter(f=>!['type','owner','airline','destination'].includes(f)))metrics.append(el('div',metric(f,a,o),'weather-highlow'));flight.append(metrics);
        if(o.showPhoto&&a.photo&&validPhoto(a.photo)){const figure=el('div',undefined,'aircraft-photo'),image=oldImages.get(a.photo.url)||el('img');image.alt='Aircraft '+(a.registration||a.callsign||a.hex);if(image.getAttribute('src')!==a.photo.url)image.src=a.photo.url;image.referrerPolicy='no-referrer';image.onerror=()=>{figure.remove();icon.hidden=false;};const credit=el('a','Photo © '+a.photo.photographer+' · Planespotters.net');credit.title=credit.textContent;credit.href=a.photo.link;credit.target='_blank';credit.rel='noopener noreferrer';figure.append(image,credit);icon.hidden=true;heading.insertBefore(figure,icon);}
        board.append(flight);
      }
    }
    const shown=o.preset==='board'?Math.min(all.length,o.maximumAircraft):Math.min(1,all.length);
    const credit=el('a',(entry.sampleAllowed&&!actual?'Sample aircraft · ':'')+(state==='stale'?'Outdated · ':state==='unavailable'?'Unavailable · ':'')+(all.length>shown?`+${all.length-shown} nearby · `:'')+'ADSB.lol · ODbL 1.0'+' · adsbdb','weather-credit');credit.href='https://www.adsb.lol/docs/open-data/api/';credit.target='_blank';credit.rel='noopener noreferrer';credit.dataset.label=credit.textContent;node.append(credit);requestAnimationFrame(()=>fit(node));
  }
  function preview(options,sampleAllowed=false,overlay=false,takeover=false){const node=el('div',undefined,'weather-card aircraft-card'),entry={options:structuredClone(options),sampleAllowed,overlay,takeover,featured:null,selectedAt:0};views.set(node,entry);paint(node,entry);resize.observe(node);return node;}
  async function refresh(){if(typeof pluginsUi!=='undefined'&&!pluginsUi.enabled('aircraft'))return;polling=true;if(fetching)return;fetching=true;try{snapshots=await api('/api/aircraft');}catch{/* Per-position age continues to expire even on an outage. */}finally{fetching=false;for(const [node,entry] of views){if(!node.isConnected){resize.unobserve(node);views.delete(node);}else paint(node,entry);}for(const [node,o] of statuses){if(!node.isConnected)statuses.delete(node);else paintStatus(node,o);}}}
  setInterval(()=>{if(polling&&document.visibilityState==='visible')refresh();},5000);
  function overlayBounds(overlay,width,height){const o=overlay.aircraft,margin=Math.min(overlay.margin,width/2,height/2),available=Math.max(0,width-margin*2),availableHeight=Math.max(0,height-margin*2),w=available*overlay.widthPercent/100,size=Math.min(o.fontSize,Math.max(12,(w-o.padding*2)/5)),rows=o.preset==='board'?o.maximumAircraft:1,columns=w-o.padding*2>=400?3:w-o.padding*2>=220?2:1,lines=o.fields.filter(f=>['type','owner','airline','destination'].includes(f)).length+Math.ceil(o.fields.filter(f=>!['type','owner','airline','destination'].includes(f)).length/columns),h=Math.min(availableHeight,Math.ceil(o.padding*2+42+(o.showPhoto&&w-o.padding*2>=180?150:0)+rows*(Math.max(size*1.7,Math.min(o.iconSize,(w-o.padding*2)*.2))+8+lines*(Math.max(12,size*.6)*1.4+2))));return {width:w,height:h,left:margin+(available-w)*overlay.x/100,top:margin+(availableHeight-h)*overlay.y/100};}
  function editor(initial,onSave,overlay=null,backgroundSlot=null){
    const opener=document.activeElement,o={...defaults(),...structuredClone(initial)},placement=overlay?structuredClone(overlay):null;
    const dialog=el('dialog',undefined,'weather-editor aircraft-editor'),form=el('form'),body=el('div',undefined,'weather-editor-body'),left=el('section'),controls=el('section');
    form.append(el('h2',overlay?'Aircraft Widget':'Aircraft tile'),el('p',overlay?'Save & apply updates this camera’s aircraft widget in every layout that shows the camera.':'Use in layout draft changes this draft. Save or apply the layout to persist it.','weather-note'),body);body.append(left,controls);dialog.append(form);
    const stage=el('div',undefined,'weather-editor-preview'),host=el('div',undefined,'weather-preview-host');left.append(stage);if(placement)stage.style.aspectRatio='16/9';
    if(backgroundSlot){const image=el('img');image.alt='Camera snapshot';dashboardUX.snapshot(image,backgroundSlot);stage.append(image);}stage.append(host);
    left.append(el('p','Sample aircraft only demonstrate the design and never trigger camera replacement. Live traffic comes from ADSB.lol; its coverage may differ from Flightradar24. Small cards omit details that do not fit.','weather-note'));
    const status=el('p','','weather-editor-state');status.setAttribute('role','status');
    function paintPreview(){host.replaceChildren(preview(o,true));if(placement){const scale=stage.clientWidth/640,b=overlayBounds({...placement,aircraft:o},640,360);Object.assign(host.style,{position:'absolute',left:b.left*scale+'px',top:b.top*scale+'px',width:b.width*scale+'px',height:b.height*scale+'px',visibility:placement.enabled?'visible':'hidden'});Object.assign(host.firstElementChild.style,{width:b.width+'px',height:b.height+'px',transformOrigin:'top left',transform:`scale(${scale})`});}}
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
    if(typeof pluginsUi==='undefined'||pluginsUi.enabled('weather'))button('Use weather location',async()=>{try{const c=await api('/api/config'),locations=[...(c.weatherOverlays||[]).map(w=>w.weather),...(c.layouts||[]).flatMap(l=>l.tiles.filter(t=>t.kind==='weather').map(t=>t.weather))];results.replaceChildren();if(!locations.length){status.textContent='No saved weather location. Search above or enter coordinates.';return;}for(const w of locations)button(w.location,()=>{for(const k of ['location','latitude','longitude']){o[k]=w[k];inputs[k].value=w[k];}results.replaceChildren();paintPreview();},results);}catch(e){status.textContent=e.message;}});
    input('Latitude','latitude','number',controls,-90,90);input('Longitude','longitude','number',controls,-180,180);
    input('Search radius (miles)','radiusMiles','number',controls,1,100);
    input('Minimum altitude (ft, optional)','minimumAltitudeFeet','number',controls,-2000,100000,true);input('Maximum altitude (ft, optional)','maximumAltitudeFeet','number',controls,-2000,100000,true);
    select('Display style','preset',[['featured','Featured flight'],['board','Flight board']]);select('Units','units',[['imperial','Feet · knots · miles'],['metric','Meters · km/h · kilometers']]);input('Maximum flights on board','maximumAircraft','number',controls,1,5);
    toggle('Show aircraft photo when available','showPhoto');
    if(placement){toggle('Hide when no aircraft nearby','hideWhenEmpty');controls.append(el('p','Off keeps the widget visible like weather. On shows it only while fresh aircraft positions match your radius and altitude filters. Waiting, empty, stale and unavailable states stay hidden.','weather-note'));}
    const fields=el('details');fields.append(el('summary','Choose aircraft information'));controls.append(fields);for(const [id,label] of Object.entries(labels)){const n=el('input');n.type='checkbox';n.checked=o.fields.includes(id);n.onchange=()=>{o.fields=n.checked?[...o.fields,id]:o.fields.filter(f=>f!==id);paintPreview();};field(label,n,fields);}
    const appearance=el('details');appearance.append(el('summary','Advanced appearance'));controls.append(appearance);
    select('Theme','theme',[['auto','Automatic (RTSPView dark)'],['dark','Dark'],['light','Light']],appearance);select('Text alignment','alignment',[['left','Left'],['center','Center'],['right','Right']],appearance);input('Accent color','accent','color',appearance);
    for(const [label,id,min,max] of [['Background opacity (%)','backgroundOpacity',0,100],['Text size','fontSize',12,64],['Icon size','iconSize',16,96],['Padding','padding',0,48],['Corner radius','cornerRadius',0,48]])slider(label,id,appearance,min,max);
    if(placement){const position=el('details');position.open=true;position.append(el('summary','Position and size'));controls.append(position);const corners=el('div',undefined,'weather-corners');position.append(corners);for(const [label,x,y] of [['Top left',0,0],['Top right',100,0],['Bottom left',0,100],['Bottom right',100,100],['Center',50,50]])button(label,()=>{placement.x=x;placement.y=y;inputs.x.value=x;inputs.y.value=y;sliderOutputs.x.textContent=x;sliderOutputs.y.textContent=y;paintPreview();},corners);for(const [label,id,min,max] of [['Maximum width (%)','widthPercent',15,95],['Horizontal position (%)','x',0,100],['Vertical position (%)','y',0,100],['Edge margin','margin',0,80]])slider(label,id,position,min,max,placement);}
    controls.append(el('p','ADSB.lol receives the search coordinates and radius. Refreshes about every 10 seconds when available, with slower retries after errors. Photos, when enabled, are requested from Planespotters.net by aircraft identifier. Owner, airline and destination lookups use adsbdb. Destinations are callsign database matches and may differ from the current flight plan.','weather-note'));
    const actions=el('div',undefined,'weather-editor-actions');form.append(status,actions);button('Cancel',()=>dialog.close(),actions);const save=el('button',overlay?'Save & apply':'Use in layout draft');save.type='submit';actions.append(save);
    form.onsubmit=async e=>{e.preventDefault();if(!o.location.trim()||!Number.isFinite(o.latitude)||!Number.isFinite(o.longitude)){status.textContent='Enter a location name and valid coordinates.';return;}if(o.minimumAltitudeFeet!=null&&o.maximumAltitudeFeet!=null&&o.minimumAltitudeFeet>o.maximumAltitudeFeet){status.textContent='Minimum altitude must not exceed maximum altitude.';return;}save.disabled=true;try{await onSave(o,placement?{...placement,aircraft:o}:null);dialog.close();}catch(error){status.textContent=error.message;}finally{save.disabled=false;}};
    const observer=new ResizeObserver(paintPreview);observer.observe(stage);dialog.onclose=()=>{observer.disconnect();dialog.remove();if(opener?.isConnected)opener.focus();};document.body.append(dialog);dialog.showModal();paintPreview();inputs.location.focus();refresh();
  }
  async function overlayEditor(slot){try{const config=await api('/api/config'),existing=(config.aircraftOverlays||[]).find(o=>o.hostCameraSlot===slot)||{hostCameraSlot:slot,enabled:true,widthPercent:40,x:100,y:100,margin:12,aircraft:{...defaults(),fields:["type","altitude","speed","distance"]}};editor(existing.aircraft,async(_,overlay)=>{const saved=await api('/api/aircraft/overlays/'+slot,{method:'PUT',body:JSON.stringify(overlay)});window.dispatchEvent(new CustomEvent('aircraft-overlay-saved',{detail:saved}));},existing,slot);}catch(e){uiDialogs.toast(e.message,'error');}}
  return {defaults,preview,editor,overlayEditor,overlayBounds,refresh,nearby,metric,shouldHide,status,replacementState};
})();
