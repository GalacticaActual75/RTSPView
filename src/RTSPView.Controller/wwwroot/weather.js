const weatherUi = (() => {
  const labels={location:'Location name',temperature:'Temperature',condition:'Condition and icon',highLow:'High / low',feelsLike:'Feels like',humidity:'Humidity',wind:'Wind',precipitation:'Rain chance',sun:'Sunrise / sunset',clock:'Date / time',hourly:'Hourly forecast',daily:'Daily forecast'};
  const presets={minimal:['temperature','condition'],compact:['location','temperature','condition','highLow'],overlay:['temperature','condition','highLow'],detailed:['location','temperature','condition','highLow','feelsLike','humidity','wind'],forecast:['location','temperature','condition','highLow','hourly'],dashboard:Object.keys(labels)};
  let snapshots=[],polling=false;const views=new Map();
  const defaults=()=>({location:'',latitude:null,longitude:null,conditionBackground:false,animateBackground:true,density:'auto',timeZone:'auto',preset:'compact',units:'imperial',theme:'auto',accent:'#F2C75C',backgroundColor:null,backgroundOpacity:80,contentOpacity:100,fontSize:24,iconSize:36,padding:14,cornerRadius:10,alignment:'left',fields:[...presets.compact]});
  const el=(tag,text,cls)=>{const n=document.createElement(tag);if(text!==undefined)n.textContent=text;if(cls)n.className=cls;return n;};
  const key=o=>Number(o.latitude).toFixed(4)+','+Number(o.longitude).toFixed(4);
  const temp=(n,o)=>n==null?'—':Math.round(o.units==='imperial'?n*1.8+32:n)+'°';
  const conditions={"0":["Clear sky","clear",0],"1":["Mainly clear","partly",1],"2":["Partly cloudy","partly",2],"3":["Overcast","cloud",3],"45":["Fog","fog",1],"48":["Depositing rime fog","fog",2],"51":["Light drizzle","rain",1],"53":["Moderate drizzle","rain",2],"55":["Dense drizzle","rain",3],"56":["Light freezing drizzle","ice",1],"57":["Dense freezing drizzle","ice",3],"61":["Slight rain","rain",1],"63":["Moderate rain","rain",2],"65":["Heavy rain","rain",3],"66":["Light freezing rain","ice",1],"67":["Heavy freezing rain","ice",3],"71":["Slight snowfall","snow",1],"73":["Moderate snowfall","snow",2],"75":["Heavy snowfall","snow",3],"77":["Snow grains","snow",1],"80":["Slight rain showers","rain",1],"81":["Moderate rain showers","rain",2],"82":["Violent rain showers","rain",3],"85":["Slight snow showers","snow",1],"86":["Heavy snow showers","snow",3],"95":["Thunderstorm","storm",2],"96":["Thunderstorm with slight hail","hail",2],"97":["Heavy thunderstorm","storm",3],"99":["Thunderstorm with heavy hail","hail",3]};
  const condition=c=>conditions[c]?.[0]||'Conditions unavailable';
  const icon=(c,day=true)=>c===0?(day?'☀':'☾'):[1,2].includes(c)?'⛅':[3,45,48].includes(c)?'☁':c>=71&&c<=77||[85,86].includes(c)?'❄':c>=95&&c<=99?'ϟ':c>=51&&c<=82?'☂':'◇';
  const time=(value,zone,options={hour:'2-digit',minute:'2-digit'})=>{try{return new Intl.DateTimeFormat(undefined,{...options,timeZone:zone==='auto'?'UTC':zone}).format(new Date(value));}catch{return '—';}};
  const sample=()=>({temperature:22.2,feelsLike:22.8,humidity:48,wind:2.7,windDirection:225,code:2,isDay:true,timeZone:'UTC',fetchedAt:new Date().toISOString(),validAt:new Date().toISOString(),daily:[{date:new Date().toISOString().slice(0,10),high:24.4,low:12.2}],hourly:[1,2,3,4,5,6].map((n)=>({time:new Date(Date.now()+n*3600000).toISOString(),temperature:22+n/2,code:2,rainChance:10}))});
  function overlayBounds(overlay, width, height) {
    const o=overlay.weather, margin=Math.min(overlay.margin,width/2,height/2),available=Math.max(0,width-margin*2),availableHeight=Math.max(0,height-margin*2);
    let w=available*overlay.widthPercent/100;
    if(o.preset==='minimal')w=Math.min(w,Math.max(160,o.fontSize*4+o.iconSize+10)+o.padding*2);
    const size=Math.min(o.fontSize,Math.max(12,(w-o.padding*2)/5)),has=f=>o.fields.includes(f);
    let h=o.padding*2+24;
    if(has('location'))h+=Math.max(12,size*.6)*1.4+2;
    if(has('temperature')||has('condition'))h+=Math.max(size*1.5,Math.min(o.iconSize,Math.max(14,(w-o.padding*2)*.2)))*1.4+2;
    if(has('condition')&&o.preset!=='minimal')h+=Math.max(12,size*.65)*1.4+2;
    if(has('highLow'))h+=Math.max(12,size*.6)*1.4+2;
    h+=['feelsLike','humidity','wind','precipitation','sun','clock'].filter(has).length*(Math.max(12,size*.55)*1.4+2);
    if(has('hourly'))h=Math.max(h+90,320+o.padding*2);
    if(has('daily'))h=Math.max(h+140,480+o.padding*2);
    h=Math.min(availableHeight,Math.ceil(h));
    return {width:w,height:h,left:margin+(available-w)*overlay.x/100,top:margin+(availableHeight-h)*overlay.y/100};
  }
  const cardResize=new ResizeObserver(entries=>{for(const {target} of entries)fitDetails(target);});
  function fitDetails(card){
    if(!card.isConnected)return;
    const options=views.get(card)?.options||{},v=widgetViewport.apply(card,options,true);widgetViewport.reset(card);
    for(const group of card.querySelectorAll('.weather-primary,.weather-secondary'))group.replaceWith(...group.children);
    card.append(...[...card.children].sort((a,b)=>Number(a.dataset.weatherOrder)-Number(b.dataset.weatherOrder)));
    const columns=v.wide&&!!card.querySelector('.weather-current')&&!!card.querySelector('.weather-highlow,.weather-location,.weather-metrics:not(:empty)');
    card.dataset.composition=columns?'columns':'stack';
    if(columns){const primary=el('div',undefined,'weather-primary'),secondary=el('div',undefined,'weather-secondary');for(const n of [...card.children])(n.matches('.weather-current,.weather-condition')?primary:secondary).append(n);card.append(primary,secondary);}
    const hide=n=>{if(n){n.hidden=true;n.dataset.responsiveHidden='';}};
    const current=card.querySelector('.weather-current');
    if(current){const reading=current.querySelector('strong');if(reading)reading.style.fontSize=v.reading+'px';}
    for(const n of card.querySelectorAll('.weather-location,.weather-clock,.weather-metrics'))if(v.detailLevel===0)hide(n);
    for(const n of card.querySelectorAll('.weather-hours,.weather-days'))if(v.detailLevel<2)hide(n);
    const sample=card.querySelector('.weather-sample');hide(sample);
    const credit=card.querySelector('.weather-credit');credit.style.fontSize='10px';
    for(const group of (columns?[...card.children]:[card]))widgetViewport.fitRows(group,card.clientHeight-2*v.padding,n=>n.matches('.weather-current')?100:n.matches('.weather-credit')?95:n.matches('.weather-highlow')?90:n.matches('.weather-condition')?80:n.matches('.weather-location')?40:20);
  }

  function applyAppearance(node,o) {
    const color=/^#[0-9a-f]{6}$/i.test(o.backgroundColor||'')?o.backgroundColor:(o.theme==='light'?'#F0F4F9':'#12161D');const bg=[1,3,5].map(i=>parseInt(color.slice(i,i+2),16)).join(',');Object.assign(node.style,{background:`rgba(${bg},${o.backgroundOpacity/100})`,padding:o.padding+'px',borderRadius:o.cornerRadius+'px',textAlign:o.alignment,'--weather-font':o.fontSize+'px','--weather-icon':o.iconSize+'px','--weather-accent':o.accent});
    node.style.setProperty('--widget-content-opacity',(o.contentOpacity??100)/100);
    node.style.setProperty('--weather-font',o.fontSize+'px');node.style.setProperty('--weather-icon',o.iconSize+'px');node.style.setProperty('--weather-accent',o.accent);
  }
  function preview(o, sampleAllowed=false) {
    const node=el('div',undefined,'weather-card'); node.dataset.preset=o.preset;node.dataset.theme=o.theme;
    applyAppearance(node,o);
    const actual=snapshots.find(s=>s.key===key(o)),s=actual||(sampleAllowed?sample():null),has=f=>o.fields.includes(f);
    const text=(value,cls)=>{const n=el('div',value,cls);node.append(n);return n;};
    if(has('location'))text(o.location||'Your location','weather-location');
    const effect=conditions[s?.code];
    if(o.conditionBackground&&effect&&s&&Date.now()-Date.parse(s.fetchedAt)<=21600000&&Date.now()-Date.parse(s.validAt)<=21600000){node.dataset.sky=effect[1];node.dataset.day=s.isDay===false?'night':'day';node.dataset.motion=o.animateBackground===false?'off':'on';node.style.setProperty('--sky-opacity',o.backgroundOpacity/100);node.style.setProperty('--sky-speed',(effect[2]===3?'.65':effect[2]===2?'1':'1.6')+'s');}
    const age=s?Date.now()-Date.parse(s.fetchedAt):Infinity;
    if(!s||age>21600000||Date.now()-Date.parse(s.validAt)>21600000)text(s?'Weather unavailable':'No weather fetched yet','weather-condition');
    else {
      const row=el('div',undefined,'weather-current');row.style.justifyContent=o.alignment==='center'?'center':o.alignment==='right'?'flex-end':'flex-start';node.append(row);
      if(has('temperature'))row.append(el('strong',temp(s.temperature,o)+(o.units==='imperial'?'F':'C')));
      else row.remove();
      const parts=new Intl.DateTimeFormat('en-CA',{timeZone:s.timeZone||'UTC',year:'numeric',month:'2-digit',day:'2-digit'}).formatToParts(new Date()),part=k=>parts.find(p=>p.type===k).value,todayDate=part('year')+'-'+part('month')+'-'+part('day'),today=s.daily?.find(d=>d.date===todayDate);
      if(has('highLow'))text('H '+temp(today?.high,o)+'  L '+temp(today?.low,o),'weather-highlow');
      if(has('condition'))text(icon(s.code,s.isDay)+' '+condition(s.code),'weather-condition');
      const metrics=el('div',undefined,'weather-metrics');node.append(metrics);
      if(has('feelsLike'))metrics.append(el('span','Feels like '+temp(s.feelsLike,o)));
      if(has('humidity'))metrics.append(el('span','Humidity '+(s.humidity??'—')+'%'));
      if(has('wind'))metrics.append(el('span','Wind '+(s.wind==null?'—':Math.round(s.wind*(o.units==='imperial'?2.236936:3.6)))+(o.units==='imperial'?' mph':' km/h')+(s.windDirection==null?'':' · '+['N','NE','E','SE','S','SW','W','NW'][(Math.round(s.windDirection/45)%8+8)%8])));
      if(has('precipitation'))metrics.append(el('span','Rain chance '+(s.hourly?.find(h=>Date.parse(h.time)<=Date.now()&&Date.parse(h.time)+3600000>Date.now())?.rainChance??'—')+'%'));
      if(has('sun'))metrics.append(el('span','Rise '+(today?.sunrise?time(today.sunrise,s.timeZone):'—')+' · Set '+(today?.sunset?time(today.sunset,s.timeZone):'—')));
      if(has('hourly')) {const hours=el('div',undefined,'weather-hours');for(const h of (s.hourly||[]).filter(h=>Date.parse(h.time)>=Date.now()).slice(0,6))hours.append(el('span',time(h.time,s.timeZone)+'\n'+icon(h.code)+'\n'+temp(h.temperature,o)));node.append(hours);}
      if(has('daily')){const days=el('div',undefined,'weather-days');for(const d of (s.daily||[]).filter(d=>d.date>todayDate).slice(0,5))days.append(el('span',new Intl.DateTimeFormat(undefined,{weekday:'short',timeZone:'UTC'}).format(new Date(d.date+'T12:00:00Z'))+'  '+icon(d.code)+'  '+temp(d.high,o)+' / '+temp(d.low,o)));node.append(days);}
      if(actual&&(age>2700000||Date.now()-Date.parse(s.validAt)>3600000||s.refreshFailed))text((age>2700000||Date.now()-Date.parse(s.validAt)>3600000?'Outdated':'Refresh failed')+' · '+Math.max(0,Math.floor(age/60000))+'m ago','weather-age');
    }
    if(has('clock'))text(time(new Date(),s?.timeZone||o.timeZone||'UTC',{weekday:'short',month:'short',day:'numeric',hour:'2-digit',minute:'2-digit'}),'weather-clock');
    const credit=el('a','Open-Meteo.com · CC BY 4.0','weather-credit');credit.href='https://open-meteo.com/';credit.target='_blank';credit.rel='noopener noreferrer';credit.dataset.label=(actual&&(age>2700000||Date.now()-Date.parse(s.validAt)>3600000)?'Outdated · ':actual&&s.refreshFailed?'Refresh failed · ':'')+credit.textContent;credit.textContent=credit.dataset.label;node.append(credit);
    if(!actual&&sampleAllowed)text('Sample weather','weather-age weather-sample');
    [...node.children].forEach((n,i)=>n.dataset.weatherOrder=String(i));
    views.set(node,{options:structuredClone(o),sampleAllowed});cardResize.observe(node);
    return node;
  }
  async function refresh(){if(typeof pluginsUi!=='undefined'&&!pluginsUi.enabled('weather'))return;polling=true;try{snapshots=await api('/api/weather');}catch{/* Cached observations still age during an outage. */}for(const [node,entry] of [...views]){views.delete(node);cardResize.unobserve(node);if(!node.isConnected)continue;const next=preview(entry.options,entry.sampleAllowed);if(node.dataset.widgetShape)next.dataset.widgetShape=node.dataset.widgetShape;for(const property of ['width','height','transform','transformOrigin'])if(node.style[property])next.style[property]=node.style[property];node.replaceWith(next);}}
  setInterval(()=>{if(polling&&document.visibilityState==='visible')refresh();},60000);
  function editor(initial,onSave,overlay=null,backgroundSlot=null,usage=[],widgetContext=null){
    const opener=document.activeElement;
    let o={...defaults(),...structuredClone(initial)},placement=overlay?structuredClone(overlay):null;
    const dialog=el('dialog',undefined,'weather-editor'),form=el('form'),title=el('h2',overlay?'Weather Widget':'Weather tile');dialog.append(form);form.append(title);form.append(el('p',overlay?'Save & apply updates this camera’s Weather Widget across all standard and automation layouts.':'Use in layout draft changes only this draft. Save or apply the layout to persist it.','weather-note'));if(overlay&&!widgetContext){const used=el('p',usage.length?'Used in: '+usage.join(', '):'Not assigned to a layout. The widget will appear wherever this camera is placed.','weather-note');form.append(used);form.append(el('p','Automation focus positions also show this widget when a rule selects this camera.','weather-note'));}
    const body=el('div',undefined,'weather-editor-body'),left=el('section'),controls=el('section');body.append(left,controls);form.append(body);
    const stage=el('div',undefined,'weather-editor-preview');if(placement)stage.style.aspectRatio=widgetContext?.output?widgetContext.output.width+'/'+widgetContext.output.height:'16/9';left.append(stage);
    if(widgetContext?.background)widgetContext.background(stage);
    if(backgroundSlot){const img=el('img');img.alt='Camera snapshot';dashboardUX.snapshot(img,backgroundSlot);stage.append(img);}
    const previewHost=el('div',undefined,'weather-preview-host');stage.append(previewHost);
    const previewNote=el('p','Preview uses sample weather until this location has been saved and fetched.','weather-note');left.append(previewNote);
    left.append(el('p','Small displays omit details that do not fit. The camera keeps playing behind a widget.','weather-note'));
    const state=el('p','','weather-editor-state');state.setAttribute('role','status');state.tabIndex=-1;
    function paint(){previewHost.replaceChildren(preview(o,true));if(widgetContext)previewHost.firstElementChild.dataset.widgetShape='landscape';if(placement){previewHost.style.visibility=placement.enabled?'visible':'hidden';previewHost.querySelector('.weather-sample')?.remove();const outputWidth=widgetContext?.output?.width||640,outputHeight=widgetContext?.output?.height||360,scale=stage.clientWidth/outputWidth,b=widgetContext?layoutWidgetsUi.bounds({...placement,weather:o},widgetContext.layout,outputWidth,outputHeight):overlayBounds({...placement,weather:o},640,360);Object.assign(previewHost.style,{position:'absolute',width:b.width/(widgetContext?layoutWidgetsUi.contentScale({...placement,weather:o},b):1)+'px',height:b.height/(widgetContext?layoutWidgetsUi.contentScale({...placement,weather:o},b):1)+'px',left:b.left*scale+'px',top:b.top*scale+'px',transformOrigin:'top left',transform:'scale('+(scale*(widgetContext?layoutWidgetsUi.contentScale({...placement,weather:o},b):1))+')'});}previewNote.textContent=snapshots.some(s=>s.key===key(o))?'Cached weather preview · '+o.location:'Sample weather preview · save to fetch your location';}
    const field=(label,input,parent=controls)=>{input.setAttribute('aria-label',label);const wrap=el('label',label);if(input.type==='checkbox'){wrap.className='weather-toggle';const track=el('span',undefined,'switch');input.setAttribute('role','switch');track.append(input,el('span'));wrap.append(track);}else if(input.type==='range'){const output=el('output',input.value),number=el('input');number.type='number';number.min=input.min;number.max=input.max;number.step=input.step||'1';number.value=input.value;number.setAttribute('aria-label',label+' value');input.setAttribute('aria-label',label+' slider');const update=input.oninput;input.oninput=()=>{output.textContent=input.value;number.value=input.value;update?.();};number.oninput=()=>{if(number.value!==''&&number.checkValidity()){input.value=number.value;output.textContent=input.value;update?.();}};wrap.append(output,input,number);}else wrap.append(input);parent.append(wrap);return input;};
    const btn=(label,action,parent=controls)=>{const b=el('button',label,'secondary');b.type='button';b.onclick=action;parent.append(b);return b;};
    const input=(label,name,type='text',parent=controls,min,max)=>{const n=el('input');n.type=type;n.value=o[name]??'';if(min!==undefined)n.min=min;if(max!==undefined)n.max=max;n.oninput=()=>{o[name]=['number','range'].includes(type)?(n.value===''?null:Number(n.value)):n.value;paint();};return field(label,n,parent);};
    if(placement){const enabled=el('input');enabled.type='checkbox';enabled.checked=placement.enabled;enabled.onchange=()=>{placement.enabled=enabled.checked;paint();};field('Enable Weather Widget',enabled);}
    const search=el('input');search.type='search';search.placeholder='City or postal code';field('Find your location',search);const results=el('div',undefined,'weather-search-results');controls.append(results);
    let searchSequence=0;
    btn('Search',async()=>{const sequence=++searchSequence;state.textContent='Searching…';try{const data=await api('/api/weather/search?q='+encodeURIComponent(search.value));if(sequence!==searchSequence||!dialog.isConnected)return;results.replaceChildren();for(const hit of data.results||[])btn([hit.name,hit.admin1,hit.country].filter(Boolean).join(', '),()=>{Object.assign(o,{location:hit.name,latitude:hit.latitude,longitude:hit.longitude,timeZone:hit.timezone||'auto'});name.value=o.location;latitude.value=o.latitude;longitude.value=o.longitude;results.replaceChildren();paint();},results);state.textContent=(data.results||[]).length?'Choose a location.':'No matches. Try a nearby city or coordinates.';}catch(e){state.textContent=e.message;}});
    const name=input('Display name','location');name.required=true;name.maxLength=80;name.addEventListener('input',()=>name.setCustomValidity(''));
    const coords=el('details');coords.append(el('summary','Enter coordinates instead'));controls.append(coords);
    const latitude=input('Latitude','latitude','number',coords,-90,90),longitude=input('Longitude','longitude','number',coords,-180,180);latitude.step=longitude.step='any';
    const select=(label,name,choices,parent=controls)=>{const n=el('select');choices.forEach(([value,text])=>n.add(new Option(text,value)));n.value=o[name];n.onchange=()=>{o[name]=n.value;paint();};field(label,n,parent);return n;};
    function toggle(label,key){const n=el('input');n.type='checkbox';n.checked=o[key];n.onchange=()=>{o[key]=n.checked;paint();};field(label,n);}
    toggle('Weather background','conditionBackground');toggle('Animate weather background','animateBackground');
    controls.append(el('p','Weather background off uses your plain background; set background opacity to 0 for no background.','weather-note'));
    const alignment=select('Text alignment','alignment',[['left','Left'],['center','Center'],['right','Right']]);
    select('Units','units',[['imperial','Fahrenheit · mph'],['metric','Celsius · km/h']]);
    select('Information density','density',[['auto','Auto · adapt to tile'],['minimal','Minimal'],['standard','Standard'],['detailed','Detailed when space allows']]);
    const preset=select('Information preset','preset',Object.keys(presets).map(k=>[k,k==='overlay'?'Widget':k[0].toUpperCase()+k.slice(1)]));
    const fields=el('details');fields.append(el('summary','Choose weather information'));controls.append(fields);
    function fieldsUi(){for(const n of [...fields.children].slice(1))n.remove();for(const [id,label] of Object.entries(labels)){const check=el('input');check.type='checkbox';check.checked=o.fields.includes(id);check.onchange=()=>{o.fields=check.checked?[...o.fields,id]:o.fields.filter(f=>f!==id);paint();};field(label,check,fields);}}
    preset.onchange=()=>{o.preset=preset.value;o.fields=[...presets[o.preset]];fieldsUi();paint();};fieldsUi();
    const appearance=el('details');appearance.append(el('summary','Advanced appearance'));controls.append(appearance);
    const paddingControl=input('Content padding','padding','range',appearance,0,48);
    appearance.append(el('p','Sets the minimum space between content and the card edges. Content stays vertically centered, so padding may not change the space above and below it. Reduce the card height to remove extra vertical space.','weather-note'));
    select('Theme','theme',[['auto','Automatic (RTSPView dark)'],['dark','Dark'],['light','Light']],appearance);

    input('Accent color','accent','color',appearance);
    const background=input('Background color','backgroundColor','color',appearance);background.value=o.backgroundColor||(o.theme==='light'?'#F0F4F9':'#12161D');
    btn('Use theme background',()=>{o.backgroundColor=null;background.value=o.theme==='light'?'#F0F4F9':'#12161D';paint();},appearance);
    for(const [label,id,min,max] of [['Background opacity (%)','backgroundOpacity',0,100],['Content opacity (%)','contentOpacity',0,100]])input(label,id,'range',appearance,min,max);
    btn('Reset appearance',()=>{const d=defaults();for(const id of ['theme','alignment','accent','backgroundColor','backgroundOpacity','contentOpacity','fontSize','iconSize','padding','cornerRadius'])o[id]=d[id];alignment.value=o.alignment;paddingControl.value=o.padding;paddingControl.parentElement.querySelector('output').textContent=o.padding;paddingControl.parentElement.querySelector('input[type=number]').value=o.padding;for(const n of appearance.querySelectorAll('label')){const c=n.querySelector('input,select');const text=n.firstChild.textContent;const map={'Theme':'theme','Text alignment':'alignment','Accent color':'accent','Background color':'backgroundColor','Background opacity (%)':'backgroundOpacity','Content opacity (%)':'contentOpacity','Text size':'fontSize','Icon size':'iconSize','Padding':'padding','Corner radius':'cornerRadius'};if(c&&map[text]){c.value=o[map[text]]??'#12161D';const output=n.querySelector('output');if(output)output.textContent=c.value;const number=n.querySelector('input[type=number]');if(number)number.value=c.value;}}paint();},appearance);
    if(placement){const group=el('details');group.open=true;group.append(el('summary','Position and size'));controls.append(group);const corners=el('div',undefined,'weather-corners');group.append(corners);for(const [label,x,y] of [['Top left',0,0],['Top right',100,0],['Bottom left',0,100],['Bottom right',100,100],['Centered',50,50]])btn(label,()=>{placement.x=x;placement.y=y;for(const n of group.querySelectorAll('input[data-placement]')){n.value=placement[n.dataset.placement];n.parentElement.querySelector('output').textContent=n.value;n.parentElement.querySelector('input[type=number]').value=n.value;}paint();},corners);
      for(const [label,id,min,max] of [['Maximum width (%)','widthPercent',15,95],['Horizontal position (%)','x',0,100],['Vertical position (%)','y',0,100],['Edge margin','margin',0,80]]){const n=el('input');n.type='range';n.min=min;n.max=max;n.value=placement[id];n.dataset.placement=id;n.oninput=()=>{placement[id]=Number(n.value);paint();};field(label,n,group);}
    }
    controls.append(el('p','Location coordinates are sent to Open-Meteo. No account or API key is required.','weather-note'));
    const actions=el('div',undefined,'weather-editor-actions');form.append(state,actions);btn('Cancel',()=>dialog.close(),actions);
    const save=el('button',overlay?'Save & apply':'Use in layout draft');save.type='submit';actions.append(save);
    form.onsubmit=async e=>{e.preventDefault();if(!o.location.trim()){name.setCustomValidity('Enter a display name.');name.reportValidity();name.focus();return;}if(!Number.isFinite(o.latitude)||!Number.isFinite(o.longitude)){state.textContent='Choose a search result or enter both coordinates.';coords.open=true;(Number.isFinite(o.latitude)?longitude:latitude).focus();return;}save.disabled=true;try{await onSave(o,placement?{...placement,weather:o}:null);dialog.close();}catch(error){state.textContent=error.message;state.focus();}finally{save.disabled=false;}};
    if(widgetContext)widgetContext(controls,placement,dialog,paint);
    const resize=new ResizeObserver(paint);resize.observe(stage);dialog.onclose=()=>{resize.disconnect();dialog.remove();if(opener?.isConnected)opener.focus();};document.body.append(dialog);dialog.showModal();paint();search.focus();refresh().then(()=>{if(dialog.isConnected)paint();});
  }
  async function overlayEditor(slot){try{const config=await api('/api/config');const existing=(config.weatherOverlays||[]).find(o=>o.hostCameraSlot===slot)||{hostCameraSlot:slot,enabled:true,widthPercent:40,x:0,y:100,margin:12,weather:{...defaults(),preset:'overlay',fields:[...presets.overlay]}};editor(existing.weather,async(_,overlay)=>{const saved=await api('/api/weather/overlays/'+slot,{method:'PUT',body:JSON.stringify(overlay)});window.dispatchEvent(new CustomEvent('weather-overlay-saved',{detail:saved}));},existing,slot,[...(config.layouts||[]).filter(l=>l.tiles.some(t=>t.cameraSlot===slot)).map(l=>l.name),...(config.automationViewLayouts||[]).filter(l=>l.tiles.some(t=>t.cameraSlot===slot)).map(l=>l.name+' (automation)')]);}catch(error){uiDialogs.toast(error.message, 'error');}}
  return {conditions,condition,fit:fitDetails,defaults,preview,editor,overlayEditor,overlayBounds,refresh,applyAppearance};
})();
