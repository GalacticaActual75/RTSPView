const systemWidgetsUi=(()=>{
  const names={weather:'Weather',aircraft:'Aircraft',systemStats:'System stats',dateTime:'Date & time'};
  let data=null,received=0,busy=false;
  const cards=new Set();
  const make=(tag,text,className)=>{const node=document.createElement(tag);if(text!==undefined)node.textContent=text;if(className)node.className=className;return node;};
  const appearance=()=>({...weatherUi.defaults(),location:'Home',latitude:0,longitude:0,padding:14,cornerRadius:10});
  const defaults=kind=>kind==='systemStats'?{appearance:appearance(),temperatureUnit:'celsius',showHeading:true}:{appearance:appearance(),timeZone:'auto',use24Hour:false,showSeconds:true,showDate:true,showWeekday:true,dateFormat:'long'};
  function clock(o,now,zone){
    const timeZone=o.timeZone==='auto'?zone:o.timeZone;
    if(!timeZone)return ['Waiting for host time',''];
    const time=new Intl.DateTimeFormat('en-US',{timeZone,hour:o.use24Hour?'2-digit':'numeric',minute:'2-digit',...(o.showSeconds?{second:'2-digit'}:{}),hourCycle:o.use24Hour?'h23':'h12'}).format(now);
    let date='';
    if(o.showDate){if(o.dateFormat==='iso'){const parts=new Intl.DateTimeFormat('en-US',{timeZone,year:'numeric',month:'2-digit',day:'2-digit'}).formatToParts(now);const part=k=>parts.find(p=>p.type===k).value;date=part('year')+'-'+part('month')+'-'+part('day');}else date=new Intl.DateTimeFormat('en-US',{timeZone,year:'numeric',month:o.dateFormat==='short'?'short':'long',day:'numeric'}).format(now);}
    if(o.showWeekday)date=new Intl.DateTimeFormat('en-US',{timeZone,weekday:'long'}).format(now)+(date?' · '+date:'');
    return [time,date];
  }
  function draw(card){
    const {kind,options:o}=card.widgetOptions,a=o.appearance,padding=Math.floor(Math.min(a.padding,Math.min(card.clientWidth,card.clientHeight)*.045)),w=Math.max(1,card.clientWidth-2*padding),h=Math.max(1,card.clientHeight-2*padding);
    card.replaceChildren();weatherUi.applyAppearance(card,{...a,padding});card.style.color=a.theme==='light'?'#111':'#fff';
    if(kind==='dateTime'){
      const current=data&&performance.now()-received<60000?new Date(Date.parse(data.timestamp)+performance.now()-received):null;
      let values=current?clock(o,current,data.timeZone):['Waiting for host time',''];
      const group=make('div',undefined,'system-clock'),time=make('div',values[0],'system-clock-time');time.style.fontSize=Math.max(12,Math.min(w/(values[0].length*.62),h*.48,180))+'px';group.append(time);
      if(values[1]&&h>=60){const date=make('div',values[1]);date.style.fontSize=Math.max(10,Math.min(w/(values[1].length*.55),h*.16,40))+'px';group.append(date);}card.append(group);return;
    }
    const sample=data?.system,age=sample?Date.parse(data.timestamp)+performance.now()-received-Date.parse(sample.timestamp):Infinity;
    const s=age<10000&&age>=-5000?sample:null,percent=v=>Number.isFinite(v)?Math.round(v)+'%':'—',temp=v=>Number.isFinite(v)?Math.round(o.temperatureUnit==='fahrenheit'?v*1.8+32:v)+(o.temperatureUnit==='fahrenheit'?'°F':'°C'):'—';
    if(o.showHeading&&h>=120){const title=make('div','System stats');title.style.fontSize=Math.max(11,Math.min(h*.09,24))+'px';card.append(title);}
    const grid=make('div',undefined,'system-stats-grid');
    for(const [label,value] of [['CPU',percent(s?.cpuPercent)],['GPU',percent(s?.gpuPercent)],['RAM',percent(s?.ramPercent)],['Memory used',Number.isFinite(s?.ramUsedGb)?s.ramUsedGb.toFixed(1)+' GB':'—'],['CPU temperature',temp(s?.cpuTemperatureC)],['GPU temperature',temp(s?.gpuTemperatureC)]]){
      const cell=make('div'),caption=make('div',label),reading=make('div',value,'system-reading');caption.style.fontSize=Math.max(9,Math.min(w/28,h/16,24))+'px';reading.style.fontSize=Math.max(12,Math.min(w/13,h/9,52))+'px';cell.append(caption,reading);grid.append(cell);
    }
    card.append(grid);card.title=s?'Live readings from the monitor host':'Waiting for current host readings. Unsupported sensors show —.';
  }
  async function poll(){
    for(const card of cards)if(!card.isConnected){card.widgetObserver.disconnect();cards.delete(card);}
    if(!cards.size||busy||document.hidden)return;
    busy=true;
    try{data=await api('/api/widget-data');received=performance.now();for(const card of cards)draw(card);}catch{/* Existing samples expire visibly. */}finally{busy=false;}
  }
  setInterval(poll,2000);
  setInterval(()=>{for(const card of cards)if(card.isConnected)draw(card);},1000);
  function preview(kind,o){const card=make('div',undefined,'system-widget-card');card.widgetOptions={kind,options:{...defaults(kind),...o}};card.widgetObserver=new ResizeObserver(()=>draw(card));card.widgetObserver.observe(card);cards.add(card);draw(card);queueMicrotask(poll);return card;}
  function editor(kind,initial,onSave,overlay,backgroundSlot,context){
    const o=structuredClone({...defaults(kind),...initial}),placement=structuredClone(overlay),dialog=make('dialog',undefined,'weather-editor'),form=make('form'),body=make('div',undefined,'weather-editor-body'),left=make('section'),controls=make('section',undefined,'weather-editor-controls'),stage=make('div',undefined,'weather-editor-preview'),host=make('div',undefined,'weather-preview-host');
    const output=context?.output||{width:1920,height:1080};stage.style.aspectRatio=output.width+'/'+output.height;stage.append(host);left.append(stage);context?.background?.(stage);body.append(left,controls);form.append(make('h2',names[kind]+' widget'),make('p','Customize this layout widget.'),body);dialog.append(form);
    const paint=()=>{host.replaceChildren(preview(kind,o));const bounds=layoutWidgetsUi.bounds({...placement,[kind]:o},context.layout,output.width,output.height),scale=stage.clientWidth/output.width;Object.assign(host.style,{position:'absolute',width:bounds.width+'px',height:bounds.height+'px',left:bounds.left*scale+'px',top:bounds.top*scale+'px',transform:'scale('+scale+')',transformOrigin:'top left'});};
    function field(label,key,type,choices,target=o){const wrap=make('label',label),input=make(choices?'select':'input');input.setAttribute('aria-label',label);if(choices)for(const [value,text]of choices)input.add(new Option(text,value));else input.type=type;if(type==='checkbox')input.checked=target[key];else input.value=target[key];input.oninput=()=>{target[key]=type==='checkbox'?input.checked:type==='number'?Number(input.value):input.value;paint();};if(type==='checkbox'){wrap.className='weather-toggle';input.setAttribute('role','switch');const track=make('span',undefined,'switch');track.append(input,make('span'));wrap.append(track);}else wrap.append(input);controls.append(wrap);return input;}
    if(kind==='systemStats'){field('Show heading','showHeading','checkbox');field('Temperature units','temperatureUnit','select',[['celsius','Celsius'],['fahrenheit','Fahrenheit']]);controls.append(make('p','Readings come from the monitor host. Unavailable or stale sensors show —.','weather-note'));}
    else{const zones=['auto','UTC',...(Intl.supportedValuesOf?.('timeZone')||['UTC','America/Los_Angeles','America/New_York','Europe/London'])];if(!zones.includes(o.timeZone))zones.push(o.timeZone);field('Time zone','timeZone','select',zones.map(z=>[z,z==='auto'?'Monitor host time zone':z]));field('24-hour time','use24Hour','checkbox');field('Show seconds','showSeconds','checkbox');field('Show date','showDate','checkbox');field('Show weekday','showWeekday','checkbox');field('Date format','dateFormat','select',[['long','September 27, 2026'],['short','Sep 27, 2026'],['iso','2026-09-27']]);}
    controls.append(make('h3','Appearance'));field('Theme','theme','select',[['auto','Dark (automatic)'],['dark','Dark'],['light','Light']],o.appearance);field('Alignment','alignment','select',[['left','Left'],['center','Center'],['right','Right']],o.appearance);field('Accent color','accent','color',null,o.appearance);
    for(const [label,key,max]of [['Background opacity (%)','backgroundOpacity',100],['Content opacity (%)','contentOpacity',100],['Padding','padding',48],['Rounded corners','cornerRadius',48]]){const input=field(label,key,'number',null,o.appearance);input.min=0;input.max=max;input.required=true;}
    const actions=make('div',undefined,'weather-editor-actions'),cancel=make('button','Cancel'),save=make('button','Use in layout draft');cancel.type='button';save.type='submit';cancel.onclick=()=>dialog.close();actions.append(cancel,save);form.append(actions);context?.(controls,placement,dialog,paint);
    form.onsubmit=e=>{e.preventDefault();onSave(o,{...placement,[kind]:o});dialog.close();};const observer=new ResizeObserver(paint);observer.observe(stage);dialog.onclose=()=>{observer.disconnect();dialog.remove();poll();};document.body.append(dialog);dialog.showModal();paint();
  }
  const adapter=kind=>({defaults:()=>defaults(kind),preview:o=>preview(kind,o),fit:()=>{},refresh:poll,overlayBounds:(o,w,h)=>({width:w,height:Math.min(h,w*.5625),left:0,top:0}),editor:(...args)=>editor(kind,...args)});
  return {names,clock,systemStats:adapter('systemStats'),dateTime:adapter('dateTime')};
})();
