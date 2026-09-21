const weatherUi = (() => {
  const labels={location:'Location name',temperature:'Temperature',condition:'Condition and icon',highLow:'High / low',feelsLike:'Feels like',humidity:'Humidity',wind:'Wind',precipitation:'Rain chance',sun:'Sunrise / sunset',clock:'Date / time',hourly:'Hourly forecast',daily:'Daily forecast'};
  const presets={minimal:['temperature','condition'],compact:['location','temperature','condition','highLow'],overlay:['temperature','condition','highLow'],detailed:['location','temperature','condition','highLow','feelsLike','humidity','wind'],forecast:['location','temperature','condition','highLow','hourly'],dashboard:Object.keys(labels)};
  let snapshots=[],polling=false;const views=new Map();
  const defaults=()=>({location:'',latitude:null,longitude:null,timeZone:'auto',preset:'compact',units:'imperial',theme:'auto',accent:'#F2C75C',backgroundOpacity:80,fontSize:24,iconSize:36,padding:14,cornerRadius:10,alignment:'left',fields:[...presets.compact]});
  const el=(tag,text,cls)=>{const n=document.createElement(tag);if(text!==undefined)n.textContent=text;if(cls)n.className=cls;return n;};
  const key=o=>Number(o.latitude).toFixed(4)+','+Number(o.longitude).toFixed(4);
  const temp=(n,o)=>n==null?'—':Math.round(o.units==='imperial'?n*1.8+32:n)+'°';
  const condition=c=>c===0?'Clear':c===1?'Mostly clear':c===2?'Partly cloudy':c===3?'Overcast':[45,48].includes(c)?'Fog':c>=51&&c<=57?'Drizzle':c>=61&&c<=67?'Rain':c>=71&&c<=77?'Snow':c>=80&&c<=82?'Rain showers':[85,86].includes(c)?'Snow showers':c>=95&&c<=99?'Thunderstorms':'Conditions unavailable';
  const icon=c=>c===0?'☀':[1,2].includes(c)?'⛅':[3,45,48].includes(c)?'☁':c>=71&&c<=77||[85,86].includes(c)?'❄':c>=95&&c<=99?'ϟ':c>=51&&c<=82?'☂':'◇';
  const time=(value,zone,options={hour:'2-digit',minute:'2-digit'})=>{try{return new Intl.DateTimeFormat(undefined,{...options,timeZone:zone==='auto'?'UTC':zone}).format(new Date(value));}catch{return '—';}};
  const sample=()=>({temperature:22.2,feelsLike:22.8,humidity:48,wind:2.7,windDirection:225,code:2,isDay:true,timeZone:'UTC',fetchedAt:new Date().toISOString(),validAt:new Date().toISOString(),daily:[{date:new Date().toISOString().slice(0,10),high:24.4,low:12.2}],hourly:[1,2,3,4,5,6].map((n)=>({time:new Date(Date.now()+n*3600000).toISOString(),temperature:22+n/2,code:2,rainChance:10}))});
  function preview(o, sampleAllowed=false) {
    const node=el('div',undefined,'weather-card'); node.dataset.preset=o.preset;node.dataset.theme=o.theme;
    const bg=o.theme==='light'?'240,244,249':'18,22,29';Object.assign(node.style,{background:`rgba(${bg},${o.backgroundOpacity/100})`,padding:o.padding+'px',borderRadius:o.cornerRadius+'px',textAlign:o.alignment,'--weather-font':o.fontSize+'px','--weather-icon':o.iconSize+'px','--weather-accent':o.accent});
    node.style.setProperty('--weather-font',o.fontSize+'px');node.style.setProperty('--weather-icon',o.iconSize+'px');node.style.setProperty('--weather-accent',o.accent);
    const actual=snapshots.find(s=>s.key===key(o)),s=actual||(sampleAllowed?sample():null),has=f=>o.fields.includes(f);
    const text=(value,cls)=>{const n=el('div',value,cls);node.append(n);return n;};
    if(has('location'))text(o.location||'Your location','weather-location');
    const age=s?Date.now()-Date.parse(s.fetchedAt):Infinity;
    if(!s||age>21600000||Date.now()-Date.parse(s.validAt)>21600000)text('Weather unavailable','weather-condition');
    else {
      const row=el('div',undefined,'weather-current');row.style.justifyContent=o.alignment==='center'?'center':o.alignment==='right'?'flex-end':'flex-start';node.append(row);
      if(has('condition'))row.append(el('span',icon(s.code),'weather-icon'));
      if(has('temperature'))row.append(el('strong',temp(s.temperature,o)+(o.units==='imperial'?'F':'C')));
      if(has('condition')&&o.preset!=='minimal')text(condition(s.code),'weather-condition');
      const today=s.daily?.find(d=>d.date===new Intl.DateTimeFormat('en-CA',{timeZone:s.timeZone||'UTC',year:'numeric',month:'2-digit',day:'2-digit'}).format(new Date()));
      if(has('highLow')&&today)text('H '+temp(today.high,o)+'  L '+temp(today.low,o),'weather-highlow');
      const metrics=el('div',undefined,'weather-metrics');node.append(metrics);
      if(has('feelsLike'))metrics.append(el('span','Feels like '+temp(s.feelsLike,o)));
      if(has('humidity'))metrics.append(el('span','Humidity '+(s.humidity??'—')+'%'));
      if(has('wind'))metrics.append(el('span','Wind '+(s.wind==null?'—':Math.round(s.wind*(o.units==='imperial'?2.236936:3.6)))+(o.units==='imperial'?' mph':' km/h')+(s.windDirection==null?'':' · '+['N','NE','E','SE','S','SW','W','NW'][(Math.round(s.windDirection/45)%8+8)%8])));
      if(has('precipitation'))metrics.append(el('span','Rain chance '+(s.hourly?.find(h=>Date.parse(h.time)<=Date.now()&&Date.parse(h.time)+3600000>Date.now())?.rainChance??'—')+'%'));
      if(has('sun'))metrics.append(el('span','Rise '+(today?.sunrise?time(today.sunrise,s.timeZone):'—')+' · Set '+(today?.sunset?time(today.sunset,s.timeZone):'—')));
      if(has('hourly')) {const hours=el('div',undefined,'weather-hours');for(const h of (s.hourly||[]).filter(h=>Date.parse(h.time)>=Date.now()).slice(0,6))hours.append(el('span',time(h.time,s.timeZone)+'\n'+icon(h.code)+'\n'+temp(h.temperature,o)));node.append(hours);}
      if(has('daily')){const days=el('div',undefined,'weather-days');for(const d of (s.daily||[]).slice(1,6))days.append(el('span',d.date.slice(5)+'  '+icon(d.code)+'  '+temp(d.high,o)+' / '+temp(d.low,o)));node.append(days);}
      if(actual&&(age>2700000||Date.now()-Date.parse(s.validAt)>3600000||s.refreshFailed))text((age>2700000||Date.now()-Date.parse(s.validAt)>3600000?'Outdated':'Refresh failed')+' · '+Math.max(0,Math.floor(age/60000))+'m ago','weather-age');
    }
    if(has('clock'))text(time(new Date(),s?.timeZone||o.timeZone||'UTC',{weekday:'short',month:'short',day:'numeric',hour:'2-digit',minute:'2-digit'}),'weather-clock');
    const credit=el('a','Open-Meteo.com · CC BY 4.0','weather-credit');credit.href='https://open-meteo.com/';credit.target='_blank';credit.rel='noopener noreferrer';node.append(credit);
    if(!actual&&sampleAllowed)text('Sample weather','weather-age');
    views.set(node,{options:structuredClone(o),sampleAllowed});
    return node;
  }
  async function refresh(){try{snapshots=await api('/api/weather');polling=true;for(const [node,entry] of [...views]){views.delete(node);if(!node.isConnected)continue;const next=preview(entry.options,entry.sampleAllowed);for(const property of ['width','height','transform','transformOrigin'])if(node.style[property])next.style[property]=node.style[property];node.replaceWith(next);}}catch{/* Preview remains usable offline. */}}
  setInterval(()=>{if(polling&&document.visibilityState==='visible')refresh();},60000);
  function editor(initial,onSave,overlay=null,backgroundSlot=null){
    let o={...defaults(),...structuredClone(initial)},placement=overlay?structuredClone(overlay):null;
    const dialog=el('dialog',undefined,'weather-editor'),form=el('form'),title=el('h2',overlay?'Weather overlay':'Weather tile');dialog.append(form);form.append(title);
    const body=el('div',undefined,'weather-editor-body'),left=el('section'),controls=el('section');body.append(left,controls);form.append(body);
    const stage=el('div',undefined,'weather-editor-preview');if(placement)stage.style.aspectRatio='16/9';left.append(stage);
    if(backgroundSlot){const img=el('img');img.alt='Camera snapshot';dashboardUX.snapshot(img,backgroundSlot);stage.append(img);}
    const previewHost=el('div',undefined,'weather-preview-host');stage.append(previewHost);
    const previewNote=el('p','Preview uses sample weather until this location has been saved and fetched.','weather-note');left.append(previewNote);
    left.append(el('p','Small displays omit details that do not fit. The camera keeps playing behind an overlay.','weather-note'));
    const state=el('p','','weather-editor-state');state.setAttribute('role','status');
    function paint(){previewHost.replaceChildren(preview(o,true));if(placement){const scale=stage.clientWidth/640,margin=placement.margin,w=Math.max(0,640-margin*2),h=Math.max(0,360-margin*2),pw=w*placement.widthPercent/100,ph=Math.min(h,['detailed','forecast','dashboard'].includes(o.preset)?400:o.preset==='minimal'?100:170);Object.assign(previewHost.style,{position:'absolute',width:pw+'px',height:ph+'px',left:(margin+(w-pw)*placement.x/100)*scale+'px',top:(margin+(h-ph)*placement.y/100)*scale+'px',transformOrigin:'top left',transform:'scale('+scale+')'});}previewNote.textContent=snapshots.some(s=>s.key===key(o))?'Cached weather preview · '+o.location:'Sample weather preview · save to fetch your location';}
    const field=(label,input,parent=controls)=>{const wrap=el('label',label);wrap.append(input);parent.append(wrap);return input;};
    const btn=(label,action,parent=controls)=>{const b=el('button',label,'secondary');b.type='button';b.onclick=action;parent.append(b);return b;};
    const input=(label,name,type='text',parent=controls,min,max)=>{const n=el('input');n.type=type;n.value=o[name]??'';if(min!==undefined)n.min=min;if(max!==undefined)n.max=max;n.oninput=()=>{o[name]=['number','range'].includes(type)?(n.value===''?null:Number(n.value)):n.value;paint();};return field(label,n,parent);};
    if(placement){const enabled=el('input');enabled.type='checkbox';enabled.checked=placement.enabled;enabled.onchange=()=>placement.enabled=enabled.checked;field('Enable weather overlay',enabled);}
    const search=el('input');search.type='search';search.placeholder='City or postal code';field('Find your location',search);const results=el('div',undefined,'weather-search-results');controls.append(results);
    let searchSequence=0;
    btn('Search',async()=>{const sequence=++searchSequence;state.textContent='Searching…';try{const data=await api('/api/weather/search?q='+encodeURIComponent(search.value));if(sequence!==searchSequence||!dialog.isConnected)return;results.replaceChildren();for(const hit of data.results||[])btn([hit.name,hit.admin1,hit.country].filter(Boolean).join(', '),()=>{Object.assign(o,{location:hit.name,latitude:hit.latitude,longitude:hit.longitude,timeZone:hit.timezone||'auto'});name.value=o.location;latitude.value=o.latitude;longitude.value=o.longitude;results.replaceChildren();paint();},results);state.textContent=(data.results||[]).length?'Choose a location.':'No matches. Try a nearby city or coordinates.';}catch(e){state.textContent=e.message;}});
    const name=input('Display name','location');name.required=true;name.maxLength=80;
    const coords=el('details');coords.append(el('summary','Enter coordinates instead'));controls.append(coords);
    const latitude=input('Latitude','latitude','number',coords,-90,90),longitude=input('Longitude','longitude','number',coords,-180,180);latitude.step=longitude.step='any';
    const select=(label,name,choices,parent=controls)=>{const n=el('select');choices.forEach(([value,text])=>n.add(new Option(text,value)));n.value=o[name];n.onchange=()=>{o[name]=n.value;paint();};field(label,n,parent);return n;};
    select('Units','units',[['imperial','Fahrenheit · mph'],['metric','Celsius · km/h']]);
    const preset=select('Style','preset',Object.keys(presets).map(k=>[k,k[0].toUpperCase()+k.slice(1)]));
    const fields=el('details');fields.append(el('summary','Choose weather information'));controls.append(fields);
    function fieldsUi(){for(const n of [...fields.children].slice(1))n.remove();for(const [id,label] of Object.entries(labels)){const check=el('input');check.type='checkbox';check.checked=o.fields.includes(id);check.onchange=()=>{o.fields=check.checked?[...o.fields,id]:o.fields.filter(f=>f!==id);paint();};field(label,check,fields);}}
    preset.onchange=()=>{o.preset=preset.value;o.fields=[...presets[o.preset]];fieldsUi();paint();};fieldsUi();
    const appearance=el('details');appearance.append(el('summary','Customize appearance'));controls.append(appearance);
    select('Theme','theme',[['auto','Automatic (RTSPView dark)'],['dark','Dark'],['light','Light']],appearance);
    select('Text alignment','alignment',[['left','Left'],['center','Center'],['right','Right']],appearance);
    input('Accent color','accent','color',appearance);
    for(const [label,id,min,max] of [['Background opacity (%)','backgroundOpacity',0,100],['Text size','fontSize',12,64],['Icon size','iconSize',16,96],['Padding','padding',0,48],['Corner radius','cornerRadius',0,48]])input(label,id,'number',appearance,min,max);
    btn('Reset appearance',()=>{const d=defaults();for(const id of ['theme','alignment','accent','backgroundOpacity','fontSize','iconSize','padding','cornerRadius'])o[id]=d[id];for(const n of appearance.querySelectorAll('label')){const c=n.querySelector('input,select');const text=n.firstChild.textContent;const map={'Theme':'theme','Text alignment':'alignment','Accent color':'accent','Background opacity (%)':'backgroundOpacity','Text size':'fontSize','Icon size':'iconSize','Padding':'padding','Corner radius':'cornerRadius'};if(c&&map[text])c.value=o[map[text]];}paint();},appearance);
    if(placement){const group=el('details');group.open=true;group.append(el('summary','Position and size'));controls.append(group);const corners=el('div',undefined,'weather-corners');group.append(corners);for(const [label,x,y] of [['Top left',0,0],['Top right',100,0],['Bottom left',0,100],['Bottom right',100,100]])btn(label,()=>{placement.x=x;placement.y=y;for(const n of group.querySelectorAll('input[data-placement]'))n.value=placement[n.dataset.placement];paint();},corners);
      for(const [label,id,min,max] of [['Width (%)','widthPercent',15,95],['Horizontal position (%)','x',0,100],['Vertical position (%)','y',0,100],['Edge margin','margin',0,80]]){const n=el('input');n.type='number';n.min=min;n.max=max;n.value=placement[id];n.dataset.placement=id;n.oninput=()=>{placement[id]=Number(n.value);paint();};field(label,n,group);}
    }
    controls.append(el('p','Location coordinates are sent to Open-Meteo. No account or API key is required.','weather-note'));
    const actions=el('div',undefined,'weather-editor-actions');form.append(state,actions);btn('Cancel',()=>dialog.close(),actions);
    const save=el('button',overlay?'Apply overlay':'Use in layout draft');save.type='submit';actions.append(save);
    form.onsubmit=async e=>{e.preventDefault();if(!o.location.trim()){name.reportValidity();return;}if(!Number.isFinite(o.latitude)||!Number.isFinite(o.longitude)){state.textContent='Choose a search result or enter both coordinates.';return;}save.disabled=true;try{await onSave(o,placement?{...placement,weather:o}:null);dialog.close();}catch(error){state.textContent=error.message;}finally{save.disabled=false;}};
    const resize=new ResizeObserver(paint);resize.observe(stage);dialog.onclose=()=>{resize.disconnect();dialog.remove();};document.body.append(dialog);dialog.showModal();paint();search.focus();refresh().then(()=>{if(dialog.isConnected)paint();});
  }
  async function overlayEditor(slot){try{const config=await api('/api/config');const existing=(config.weatherOverlays||[]).find(o=>o.hostCameraSlot===slot)||{hostCameraSlot:slot,enabled:true,widthPercent:40,x:0,y:100,margin:12,weather:{...defaults(),preset:'overlay',fields:[...presets.overlay]}};editor(existing.weather,async(_,overlay)=>{await api('/api/weather/overlays/'+slot,{method:'PUT',body:JSON.stringify(overlay)});},existing,slot);}catch(error){alert(error.message);}}
  function attach(){for(const card of document.querySelectorAll('#cameras .camera-card[data-slot]')){const slot=Number(card.dataset.slot);if(![1,2,3,4,5,6,7,8,9,26,27,28,29,30,31,32].includes(slot)||card.querySelector('.weather-open'))continue;const b=el('button','Weather overlay','secondary weather-open');b.type='button';b.onclick=()=>overlayEditor(slot);(card.querySelector('.actions')||card).append(b);}}
  return {defaults,preview,editor,attach,overlayEditor,refresh};
})();
