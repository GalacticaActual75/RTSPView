// Isolated browser fixture; never starts the Controller or touches installed settings.
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../src/RTSPView.Controller/wwwroot');
const slots = [1,2,3,4,5,6,7,8,9,26,27,28,29,30,31,32];
const camera = (slot,name) => ({slot,name,enabled:true,rtspUrl:'',transport:1,networkCacheMilliseconds:1000,startupTimeoutSeconds:20,watchdogTimeoutSeconds:20,maximumReconnectBackoffSeconds:30,lowLatency:false,decodeAudio:false});
const overlay = (slot,name,hostCameraSlot) => ({hostCameraSlot,camera:camera(slot,name),viewportShape:0,viewportWidthPercent:50,viewportHeightPercent:50,viewportHorizontalPositionPercent:0,viewportVerticalPositionPercent:100,viewportOpacityPercent:100,zoomPercent:100,imageHorizontalPositionPercent:50,imageVerticalPositionPercent:50,customViewportPathData:'',customViewportSourceName:'',customViewportViewBoxX:0,customViewportViewBoxY:0,customViewportViewBoxWidth:1,customViewportViewBoxHeight:1,customViewportRotationDegrees:0});
let config = {schemaVersion:15,cameraCount:9,cameras:slots.map((slot,i)=>camera(slot,'Camera '+(i+1))),doorbellOverlay:overlay(10,'Doorbell',2),garageOverlay:overlay(11,'Garage',3),additionalOverlays:[overlay(12,'Patio',1)],layouts:[{id:'default',name:'Default',rows:3,columns:3,tiles:slots.slice(0,9).map((cameraSlot,i)=>({cameraSlot,row:Math.floor(i/3),column:i%3,rowSpan:1,columnSpan:1}))}],activeLayoutId:'default',startFullScreen:true,preferredMonitor:0,hideMouseCursor:true,mouseCursorHideSeconds:3,showCameraNames:true,showCameraStats:true,showTileBorders:true,keepViewerAlwaysOnTop:true};
config.automationViewLayouts = [{id:'focus',name:'One large camera',rows:3,columns:3,focusSlots:[-1],tiles:[{cameraSlot:-1,row:0,column:0,rowSpan:2,columnSpan:2},{cameraSlot:2,row:0,column:2,rowSpan:1,columnSpan:1},{cameraSlot:3,row:1,column:2,rowSpan:1,columnSpan:1}]}];
config.deletedOverlaySlots=[]; config.deletedCameraSlots=[];
if(process.env.RTSPVIEW_WIDGET_FIXTURE==='1'){
  config.plugins={weather:true,aircraft:true,systemStats:true,dateTime:true};
  const appearance={density:'auto',theme:'dark',accent:'#F2C75C',backgroundOpacity:90,fontSize:24,iconSize:36,padding:14,cornerRadius:10,alignment:'left',units:'imperial',location:'Test city',latitude:0,longitude:0};
  config.layouts[0].widgets=[{id:'weather-test',kind:'weather',enabled:true,hostCameraSlot:0,margin:0,widthPercent:33,heightPercent:33,x:0,y:0,cell:{row:0,column:1,rowSpan:2,columnSpan:2},weather:{...appearance,timeZone:'auto',preset:'compact',fields:['location','temperature','condition','highLow']}},{id:'aircraft-test',kind:'aircraft',enabled:true,hostCameraSlot:0,margin:0,widthPercent:33,heightPercent:33,x:100,y:100,cell:{row:2,column:2,rowSpan:1,columnSpan:1},aircraft:{...appearance,radiusMiles:10,preset:'featured',cardDesign:'compact',maximumAircraft:2,textSizes:{},showPhoto:true,fields:['type','owner','altitude','speed','distance']}}];
}
const schedules=Object.fromEntries(['viewer','host'].map(action=>[action,{settings:{enabled:action==='host',action,mode:'weekly',intervalHours:24,time:'03:00',days:[0]},nextRun:'2026-10-01T03:00:00Z',result:'Schedule saved.'}]));
http.createServer(async(req,res)=>{
  const url=new URL(req.url,'http://localhost');res.setHeader('Cache-Control','no-store');
  const json=value=>{res.setHeader('Content-Type','application/json');res.end(JSON.stringify(value));};
  if(url.pathname==='/api/plugins'){if(req.method==='PUT'){let text='';for await(const chunk of req)text+=chunk;config.plugins=JSON.parse(text);}return json(config.plugins||{});}
  if(url.pathname==='/api/widget-data')return json({timestamp:new Date().toISOString(),timeZone:'America/Los_Angeles',system:{timestamp:new Date().toISOString(),cpuPercent:37,gpuPercent:24,ramPercent:62,ramUsedGb:19.8,ramTotalGb:32,cpuTemperatureC:58,gpuTemperatureC:64}});
  if(url.pathname==='/api/weather/search'||url.pathname==='/api/aircraft/search')return json({results:[{name:'Test city',latitude:0,longitude:0,timezone:'UTC'}]});
  if(url.pathname==='/api/weather')return json([{key:'0.0000,0.0000',fetchedAt:new Date().toISOString(),validAt:new Date().toISOString(),temperature:22,code:2,timeZone:'UTC',daily:[{date:new Date().toISOString().slice(0,10),high:25,low:15}]}]);
  if(url.pathname==='/api/aircraft')return json([{key:'0.0000,0.0000,10.0',fetchedAt:new Date().toISOString(),aircraft:[{hex:'abc123',callsign:'TEST123',registration:'N123EX',type:'B738',modelName:'Boeing 737-800',registeredOwner:'Example owner',latitude:.01,longitude:.01,altitudeFeet:12000,speedKnots:280,positionAt:new Date().toISOString()}]}]);
  if(url.pathname==='/api/restart-schedule'){
    if(req.method==='PUT'){let body='';for await(const chunk of req)body+=chunk;const data=JSON.parse(body);if(data.settings.enabled&&data.settings.action==='host'&&!data.hostAcknowledged){res.statusCode=400;return json({error:'Acknowledgment required'});}schedules[data.settings.action].settings=data.settings;}
    return json({schedules,timeZone:'UTC',timeZoneId:'UTC',serverTime:new Date().toISOString()});
  }
  if(url.pathname==='/api/dependencies/pawnio')return json({available:true,pawnInstalled:true,state:'ready',message:'Ready.'});
  if(url.pathname==='/api/temperatures')return json({settings:{showWarnings:false,cpuWarningEnabled:false,gpuWarningEnabled:false,cpuMaxC:90,gpuMaxC:85},cpuC:null,gpuC:null});
  if(url.pathname==='/api/display'&&req.method==='PUT'){let body='';for await(const chunk of req)body+=chunk;Object.assign(config,JSON.parse(body));return json(config);}
  if(['/api/doorbell','/api/garage','/api/overlays/12'].includes(url.pathname)&&req.method==='PUT'){let body='';for await(const chunk of req)body+=chunk;const data=JSON.parse(body);if(url.pathname==='/api/doorbell')config.doorbellOverlay=data;else if(url.pathname==='/api/garage')config.garageOverlay=data;else config.additionalOverlays[0]=data;return json(data);}
  if(url.pathname==='/api/session')return json({authenticated:true,csrfToken:'fixture'});
  if(url.pathname==='/api/status')return json({hostname:'Isolated beta preview',lanAddresses:['localhost'],version:'1.0.43-beta.6',currentTime:new Date().toISOString()});
  if(/^\/api\/cameras\/\d+$/.test(url.pathname)&&req.method==='PUT'){
    let body='';for await(const chunk of req)body+=chunk;const data=JSON.parse(body);
    const index=config.cameras.findIndex(c=>c.slot===Number(url.pathname.split('/').at(-1)));
    config.cameras[index]=data;return json(data);
  }
  if(url.pathname.endsWith('/thumbnail/refresh'))return json({message:'Synthetic snapshot captured.'});
  if(url.pathname==='/api/network')return json({enabled:true,managed:true,addresses:['http://192.0.2.4:5080','http://192.0.2.148:5080'],message:'Example addresses for layout testing only.'});
  if(url.pathname==='/api/snapshots/settings'&&req.method==='PUT'){let body='';for await(const chunk of req)body+=chunk;config.snapshots=JSON.parse(body);return json(config.snapshots);}
  if(url.pathname==='/api/config')return json(config);
  if(url.pathname==='/api/automation'){
    if(req.method==='PUT'){let body='';for await(const chunk of req)body+=chunk;config.automation=JSON.parse(body);}
    return json({settings:config.automation||{enabled:false,host:'',port:1883,tls:false,authenticate:false,username:'',clientId:'fixture',rules:[]},hasPassword:false});
  }
  if(url.pathname==='/api/automation/status')return json({connection:'Disabled',rules:[]});
  if(url.pathname==='/api/automation/layouts'){
    if(req.method==='PUT'){let body='';for await(const chunk of req)body+=chunk;config.automationViewLayouts=JSON.parse(body).layouts;}
    return json({layouts:config.automationViewLayouts});
  }

  if(url.pathname==='/api/cameras'&&req.method==='POST'){
    if(config.cameraCount>=16){res.statusCode=400;return json({error:'The maximum of 16 cameras has been reached.'});}
    return json(config.cameras[config.cameraCount++]);
  }
  if(url.pathname==='/api/layouts'&&req.method==='PUT'){
    let body='';for await(const chunk of req)body+=chunk;
    const request=JSON.parse(body);config={...config,...request};return json(request);
  }
  if(url.pathname==='/api/telemetry')return json({system:{},viewer:null,viewerConnected:false});
  if(url.pathname==='/api/logs')return json({lines:['Isolated fixture — no live camera connections.']});
  if(url.pathname==='/api/update')return json({installedVersion:'1.0.30',installedChannel:'stable',selectedChannel:'stable',updateAvailable:false});
  if(url.pathname.endsWith('/thumbnail')){res.setHeader('Content-Type','image/svg+xml');return res.end('<svg xmlns="http://www.w3.org/2000/svg" width="640" height="360"><rect width="640" height="360" fill="#162638"/><path d="M0 310L170 140L260 230L430 70L640 290V360H0Z" fill="#284c61"/><circle cx="520" cy="75" r="30" fill="#558da2"/></svg>');}
  const file=path.resolve(root,'.'+(url.pathname==='/'?'/index.html':url.pathname));
  if(!file.startsWith(root+path.sep)||!fs.existsSync(file)){res.statusCode=404;return res.end();}
  res.setHeader('Content-Type',({'.html':'text/html','.js':'text/javascript','.css':'text/css','.png':'image/png','.ico':'image/x-icon'})[path.extname(file)]||'application/octet-stream');fs.createReadStream(file).pipe(res);
}).listen(5097,'127.0.0.1',()=>console.log('Layout fixture: http://127.0.0.1:5097'));
